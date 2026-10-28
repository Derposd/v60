using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Session;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class SystemTweaksTests
{
    private sealed class NullCommands : ICommandRunner
    {
        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null) => new(0, string.Empty, string.Empty);
    }

    internal sealed class FakeServices : IServiceControl
    {
        public Dictionary<string, ServiceState> All { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls { get; } = new();

        public ServiceState? Query(string name) => All.TryGetValue(name, out var s) ? s : null;

        public void SetStart(string name, string start) => All[name] = All[name] with { Start = start };

        public bool Stop(string name)
        {
            Calls.Add("stop " + name);
            All[name] = All[name] with { Running = false };
            return true;
        }

        public bool Start(string name)
        {
            Calls.Add("start " + name);
            All[name] = All[name] with { Running = true };
            return true;
        }
    }

    private readonly InMemoryRegistry _reg = new();
    private readonly InMemoryFileSystem _fs = new();

    private TweakContext Ctx(SystemSnapshot? s = null) => new()
    {
        Snapshot = s ?? new SystemSnapshot { Windows = new WindowsInfo("Windows 11", "24H2", 26100, true) },
        Registry = _reg,
        FileSystem = _fs,
        Commands = new NullCommands(),
    };

    [Fact]
    public void Catalog_ids_are_unique_and_every_tweak_explains_itself()
    {
        var all = SystemTweakCatalog.All;
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.Explanation)));
        Assert.Contains(all, t => t.Category == TweakCategory.Privacy);
        Assert.Contains(all, t => t.Category == TweakCategory.Services);
        Assert.Contains(all, t => t.Category == TweakCategory.Interface);
    }

    [Fact]
    public void Registry_tweak_writes_only_values_that_differ()
    {
        var tweak = (RegistryTweak)SystemTweakCatalog.Find("ui.explorer")!;
        _reg.SetValue(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", RegValue.Dword(0));

        var plan = tweak.Plan(Ctx());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.Equal(tweak.Settings.Count - 1, plan.Actions.Count);
        Assert.DoesNotContain(plan.Actions, a => a.ValueName == "HideFileExt");
        Assert.Contains("частично", plan.StatusText);
    }

    [Fact]
    public void Registry_tweak_is_applied_when_all_values_match_and_hklm_needs_admin()
    {
        var tweak = (RegistryTweak)SystemTweakCatalog.Find("priv.widgets")!;
        Assert.True(tweak.Plan(Ctx()).RequiresAdmin);

        foreach (var s in tweak.Settings)
        {
            _reg.SetValue(s.Hive, s.Key, s.Name, s.Value);
        }

        Assert.Equal(TweakState.Applied, tweak.DetectState(Ctx()));
    }

    [Fact]
    public void Windows_version_limits_are_respected()
    {
        var win10 = new SystemSnapshot { Windows = new WindowsInfo("Windows 10", "22H2", 19045, false) };
        Assert.Equal(TweakStatus.NotApplicable, SystemTweakCatalog.Find("priv.copilot")!.Plan(Ctx(win10)).Status);
        Assert.Equal(TweakStatus.CanApply, SystemTweakCatalog.Find("priv.cortana")!.Plan(Ctx(win10)).Status);
        Assert.Equal(TweakStatus.NotApplicable, SystemTweakCatalog.Find("priv.cortana")!.Plan(Ctx()).Status);
    }

    [Fact]
    public void Service_tweak_moves_installed_services_to_manual_and_skips_missing()
    {
        var services = new FakeServices();
        services.All["DiagTrack"] = new ServiceState("DiagTrack", ServiceStart.Auto, true);
        ServiceTweak.ServiceSource = _ => services;
        try
        {
            var plan = SystemTweakCatalog.Find("svc.telemetry")!.Plan(Ctx());

            Assert.Equal(TweakStatus.CanApply, plan.Status);
            var svc = Assert.Single(plan.Actions, a => a.Kind == ActionKind.SetService);
            Assert.Equal("DiagTrack", svc.ServiceName);
            Assert.Equal(ServiceStart.Manual, svc.ServiceStartType);
            Assert.True(svc.StopService);
            Assert.Contains(plan.Actions, a => a.ValueName == "AllowTelemetry");

            services.All["DiagTrack"] = new ServiceState("DiagTrack", ServiceStart.Manual, Running: true);
            _reg.SetValue(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", RegValue.Dword(1));
            Assert.Equal(TweakStatus.AlreadyOptimal, SystemTweakCatalog.Find("svc.telemetry")!.Plan(Ctx()).Status);
        }
        finally
        {
            ServiceTweak.ServiceSource = ctx => new ServiceControl(ctx.Registry, ctx.Commands);
        }
    }

    [Fact]
    public void Search_indexing_is_not_offered_on_ssd_only_pcs()
    {
        var ssd = new SystemSnapshot { Disks = new[] { new DiskInfo("NVMe", DiskKind.Nvme, 1) } };
        Assert.Equal(TweakStatus.NotApplicable, SystemTweakCatalog.Find("svc.wsearch")!.Plan(Ctx(ssd)).Status);
    }

    [Fact]
    public void Printer_detection_ignores_virtual_printers()
    {
        const string key = @"SYSTEM\CurrentControlSet\Control\Print\Printers";
        _reg.SetValue(RegHive.LocalMachine, key + @"\Microsoft Print to PDF", "Name", RegValue.Str("x"));
        Assert.False(Printers.HasRealPrinter(_reg));
        _reg.SetValue(RegHive.LocalMachine, key + @"\HP LaserJet", "Name", RegValue.Str("x"));
        Assert.True(Printers.HasRealPrinter(_reg));
    }

    [Fact]
    public void Ultimate_plan_is_created_with_a_known_guid_so_undo_deletes_that_copy()
    {
        var s = new SystemSnapshot
        {
            PowerPlan = new PowerPlanInfo(PowerPlanTweak.Balanced, "Сбалансированная", new[] { (PowerPlanTweak.Balanced, "Сбалансированная") }),
        };
        var plan = new UltimatePlanTweak().Plan(Ctx(s));

        Assert.Equal(2, plan.Actions.Count);
        var create = plan.Actions[0];
        var guid = create.Arguments!.Split(' ')[2];
        Assert.Equal($"/delete {guid}", create.UndoArguments);
        Assert.Equal($"/setactive {guid}", plan.Actions[1].Arguments);
        Assert.Equal($"/setactive {PowerPlanTweak.Balanced}", plan.Actions[1].UndoArguments);
    }

    [Fact]
    public void Startup_entries_are_listed_and_switched_through_startup_approved()
    {
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        _reg.SetValue(RegHive.CurrentUser, run, "Discord", RegValue.Str("\"C:\\Users\\u\\Discord\\Update.exe\" --processStart Discord.exe"));
        _reg.SetValue(RegHive.CurrentUser, run, "OneDrive", RegValue.Str(@"C:\Program Files\OneDrive\OneDrive.exe /background"));
        _reg.SetValue(RegHive.CurrentUser, approved, "OneDrive", new RegValue(RegKind.Binary, "03000000AABBCCDDEEFF0011"));
        _fs.WriteAllText(@"C:\Startup\Tool.lnk", "x");
        _fs.WriteAllText(@"C:\Startup\desktop.ini", "x");

        var list = new StartupManager(_reg, _fs, @"C:\Startup", @"C:\CommonStartup").List();

        Assert.Equal(new[] { "Discord", "OneDrive", "Tool.lnk" }, list.Select(e => e.Name));
        Assert.True(list[0].Enabled);
        Assert.False(list[1].Enabled);
        Assert.Equal(@"C:\Users\u\Discord\Update.exe", list[0].ExePath);
        Assert.Equal(@"C:\Program Files\OneDrive\OneDrive.exe", list[1].ExePath);

        var action = StartupManager.Switch(list[0], enable: false);
        Assert.Equal(approved, action.Key);
        Assert.Equal(StartupManager.DisabledValue, action.Value!.Data);
        Assert.False(action.RequiresAdmin);
    }

    // ---------- real-time game mode ----------

    private sealed class FakeProcesses : IProcessControl
    {
        public List<RunningProcess> Running { get; } = new();
        public List<string> Log { get; } = new();
        public bool AllowPriority { get; set; } = true;

        public IReadOnlyList<string> RunningNames() => Running.Select(p => p.Name).ToList();
        public IReadOnlyList<RunningProcess> Find(string name) => Running.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

        public bool Close(int pid, TimeSpan timeout)
        {
            Log.Add($"close {pid}");
            Running.RemoveAll(p => p.Pid == pid);
            return true;
        }

        public bool Start(string path, string? arguments)
        {
            Log.Add($"start {path} {arguments}".Trim());
            return true;
        }

        public bool SetHighPriority(string name)
        {
            Log.Add($"high {name}");
            return AllowPriority;
        }

        public bool SetNormalPriority(string name)
        {
            Log.Add($"normal {name}");
            return true;
        }
    }

    private static GameModeConfig Config() => new()
    {
        Enabled = true,
        Processes = { "OneDrive", "steam", "explorer" },
        Services = { "WSearch", "Audiosrv" },
    };

    [Fact]
    public void Game_start_closes_listed_programs_and_game_exit_restores_them()
    {
        var procs = new FakeProcesses();
        procs.Running.Add(new RunningProcess(10, "OneDrive", @"C:\OneDrive\OneDrive.exe", "\"C:\\OneDrive\\OneDrive.exe\" /background"));
        procs.Running.Add(new RunningProcess(11, "steam", @"C:\Steam\steam.exe", null));
        procs.Running.Add(new RunningProcess(12, "explorer", @"C:\Windows\explorer.exe", null));
        var services = new FakeServices();
        services.All["WSearch"] = new ServiceState("WSearch", ServiceStart.DelayedAuto, true);
        services.All["Audiosrv"] = new ServiceState("Audiosrv", ServiceStart.Auto, true);
        var session = new GameSessionManager(procs, services, _fs, @"C:\state.json");

        Assert.Empty(session.Tick(Config()));
        procs.Running.Add(new RunningProcess(20, "cs2", @"C:\cs2.exe", null));

        var started = session.Tick(Config());
        Assert.Contains("Counter-Strike 2", started[0]);
        Assert.Equal(new[] { "close 10", "high cs2" }, procs.Log);
        Assert.Equal(new[] { "stop WSearch" }, services.Calls);
        Assert.True(session.Active);
        Assert.Empty(session.Tick(Config())); // still running: nothing new

        procs.Running.RemoveAll(p => p.Name == "cs2");
        var ended = session.Tick(Config());

        Assert.Contains("возвращено программ — 1, служб — 1", ended[0]);
        Assert.Contains("start C:\\OneDrive\\OneDrive.exe /background", procs.Log);
        Assert.Contains("start WSearch", services.Calls);
        Assert.False(session.Active);
    }

    [Fact]
    public void Session_left_by_a_crash_is_restored_on_next_start()
    {
        var procs = new FakeProcesses();
        procs.Running.Add(new RunningProcess(10, "OneDrive", @"C:\OneDrive\OneDrive.exe", null));
        procs.Running.Add(new RunningProcess(20, "dota2", @"C:\dota2.exe", null));
        var services = new FakeServices();
        var first = new GameSessionManager(procs, services, _fs, @"C:\state.json");
        first.Tick(Config());
        Assert.True(_fs.FileExists(@"C:\state.json"));

        // The app died; the game was closed meanwhile.
        procs.Running.RemoveAll(p => p.Name == "dota2");
        var second = new GameSessionManager(procs, services, _fs, @"C:\state.json");
        var msg = second.RecoverAfterCrash();

        Assert.Contains("Восстановление", msg[0]);
        Assert.Contains("start C:\\OneDrive\\OneDrive.exe", procs.Log);
        Assert.False(second.Active);
    }

    [Fact]
    public void Refused_priority_is_reported_and_not_restored()
    {
        var procs = new FakeProcesses { AllowPriority = false };
        procs.Running.Add(new RunningProcess(20, "r5apex", @"C:\r5apex.exe", null));
        var session = new GameSessionManager(procs, new FakeServices(), _fs, @"C:\state.json");

        var msg = session.Tick(Config());

        Assert.Contains(msg, m => m.Contains("Приоритет игры не изменён"));
        Assert.Null(session.Current!.PriorityRaised);
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\" /background", "/background")]
    [InlineData("C:\\x.exe -silent", "-silent")]
    [InlineData("\"C:\\x.exe\"", null)]
    public void Arguments_are_taken_from_the_recorded_command_line(string cmd, string? args) =>
        Assert.Equal(args, GameSessionManager.ArgumentsOf(new RunningProcess(1, "x", "C:\\x.exe", cmd)));
}
