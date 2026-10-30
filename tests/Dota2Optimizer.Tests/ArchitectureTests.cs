using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class ArchitectureTests
{
    private sealed class FakeServices : IServiceControl
    {
        public Dictionary<string, ServiceState> All { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ServiceState? Query(string name) => All.TryGetValue(name, out var s) ? s : null;
        public void SetStart(string name, string start) => All[name] = All[name] with { Start = start };

        public bool Stop(string name)
        {
            All[name] = All[name] with { Running = false };
            return true;
        }

        public bool Start(string name)
        {
            All[name] = All[name] with { Running = true };
            return true;
        }
    }

    private sealed class ScRunner : ICommandRunner
    {
        public List<string> Calls { get; } = new();
        public string QueryOutput { get; set; } = string.Empty;

        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null)
        {
            Calls.Add($"{fileName} {arguments}");
            return new CommandResult(0, arguments.StartsWith("query", StringComparison.Ordinal) ? QueryOutput : string.Empty, string.Empty);
        }
    }

    /// <summary>A minimal registry tweak: sets HKCU\Software\Test\A = 1.</summary>
    private sealed class SampleTweak : Tweak
    {
        public override string Id => "test.sample";
        public override string Title => "Sample";
        public override TweakCategory Category => TweakCategory.Windows;
        public override Effectiveness Effectiveness => Effectiveness.Low;
        public override string Explanation => "test";

        public override TweakPlan Plan(TweakContext ctx) =>
            ctx.Registry.GetValue(RegHive.CurrentUser, @"Software\Test", "A")?.AsInt() == 1
                ? Result(TweakStatus.AlreadyOptimal, "on")
                : Result(TweakStatus.CanApply, "off", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1), "A = 1") });
    }

    private readonly InMemoryFileSystem _fs = new();
    private readonly InMemoryRegistry _reg = new();
    private readonly FakeServices _services = new();
    private readonly ChangeExecutor _exec;

    public ArchitectureTests()
    {
        AppPaths.OverrideRoot(Path.Combine(Path.GetTempPath(), "d2o-tests"));
        _exec = new ChangeExecutor(_fs, _reg, new ScRunner(), new BackupStore(_fs, @"C:\backup"), _services);
    }

    private TweakContext Ctx() => new() { Snapshot = new SystemSnapshot(), Registry = _reg, FileSystem = _fs, Commands = new ScRunner() };

    [Fact]
    public void Service_start_type_and_running_state_are_restored()
    {
        _services.All["DiagTrack"] = new ServiceState("DiagTrack", ServiceStart.Auto, Running: true);

        var r = _exec.Apply("t", "Telemetry", new[] { PlannedAction.SetService("DiagTrack", ServiceStart.Manual, stop: true, "DiagTrack → вручную") }, false);
        Assert.True(r.Success);
        Assert.Equal(new ServiceState("DiagTrack", ServiceStart.Manual, false), _services.All["DiagTrack"]);

        Assert.True(_exec.Rollback(r.ChangeSetId!).Success);
        Assert.Equal(new ServiceState("DiagTrack", ServiceStart.Auto, true), _services.All["DiagTrack"]);
    }

    [Fact]
    public void A_stopped_service_is_not_started_by_undo()
    {
        _services.All["WSearch"] = new ServiceState("WSearch", ServiceStart.DelayedAuto, Running: false);

        var r = _exec.Apply("t", "Search", new[] { PlannedAction.SetService("WSearch", ServiceStart.Manual, stop: false, "WSearch → вручную") }, false);
        _exec.Rollback(r.ChangeSetId!);

        Assert.Equal(new ServiceState("WSearch", ServiceStart.DelayedAuto, false), _services.All["WSearch"]);
    }

    [Fact]
    public void Missing_service_fails_the_set_without_changes()
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(0));
        var r = _exec.Apply("t", "Mixed", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1), "A"),
            PlannedAction.SetService("NoSuchService", ServiceStart.Manual, stop: true, "missing"),
        }, false);

        Assert.False(r.Success);
        Assert.Equal("0", _reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A")!.Data);
    }

    [Fact]
    public void Key_created_by_a_tweak_is_removed_on_undo_but_an_existing_one_stays()
    {
        _reg.CreateKey(RegHive.CurrentUser, @"Software\Existing");
        var r = _exec.Apply("t", "Keys", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Policies\New", "X", RegValue.Dword(1), "new key"),
            PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Existing", "Y", RegValue.Dword(1), "old key"),
        }, false);
        Assert.True(_reg.KeyExists(RegHive.CurrentUser, @"Software\Policies\New"));

        _exec.Rollback(r.ChangeSetId!);

        Assert.False(_reg.KeyExists(RegHive.CurrentUser, @"Software\Policies\New"));
        Assert.True(_reg.KeyExists(RegHive.CurrentUser, @"Software\Existing"));
    }

    [Fact]
    public void Copied_file_replaces_target_and_undo_restores_original_bytes()
    {
        _fs.WriteAllBytes(@"C:\Game\dxgi.dll", new byte[] { 1, 2, 3 });
        _fs.WriteAllBytes(@"C:\Downloads\OptiScaler.dll", new byte[] { 9, 9 });

        var r = _exec.Apply("t", "Copy", new[] { PlannedAction.CopyFile(@"C:\Downloads\OptiScaler.dll", @"C:\Game\dxgi.dll", "copy") }, false);
        Assert.Equal(new byte[] { 9, 9 }, _fs.ReadAllBytes(@"C:\Game\dxgi.dll"));

        _exec.Rollback(r.ChangeSetId!);
        Assert.Equal(new byte[] { 1, 2, 3 }, _fs.ReadAllBytes(@"C:\Game\dxgi.dll"));
    }

    [Fact]
    public void Copied_file_that_was_new_is_deleted_on_undo()
    {
        _fs.WriteAllBytes(@"C:\Downloads\a.dll", new byte[] { 1 });
        var r = _exec.Apply("t", "Copy", new[] { PlannedAction.CopyFile(@"C:\Downloads\a.dll", @"C:\Game\winmm.dll", "copy") }, false);

        _exec.Rollback(r.ChangeSetId!);

        Assert.False(_fs.FileExists(@"C:\Game\winmm.dll"));
    }

    [Fact]
    public void Preview_shows_service_before_and_after()
    {
        _services.All["SysMain"] = new ServiceState("SysMain", ServiceStart.Auto, Running: true);
        var line = _exec.Preview(new[] { PlannedAction.SetService("SysMain", ServiceStart.Manual, stop: true, "x") }).Single();

        Assert.Equal("автоматически, работает", line.Before);
        Assert.Equal("вручную, остановить", line.After);
        Assert.True(line.RequiresAdmin);
    }

    [Theory]
    [InlineData("SERVICE_NAME: DiagTrack\r\n        TYPE               : 10  WIN32_OWN_PROCESS\r\n        STATE              : 4  RUNNING\r\n        WIN32_EXIT_CODE    : 0  (0x0)", true)]
    [InlineData("SERVICE_NAME: WSearch\r\n        TYPE               : 10  WIN32_OWN_PROCESS\r\n        STATE              : 1  STOPPED\r\n        WIN32_EXIT_CODE    : 1077  (0x435)", false)]
    [InlineData("ИМЯ_СЛУЖБЫ: DiagTrack\r\n        ТИП                : 10  WIN32_OWN_PROCESS\r\n        СОСТОЯНИЕ          : 4  RUNNING", true)]
    public void Sc_query_state_is_parsed_in_any_language(string output, bool running) =>
        Assert.Equal(running, ServiceControl.ParseRunning(output));

    [Fact]
    public void Service_control_reads_start_type_from_registry_and_refuses_drivers()
    {
        var reg = new InMemoryRegistry();
        var cmd = new ScRunner { QueryOutput = "STATE : 4  RUNNING" };
        reg.SetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", RegValue.Dword(2));
        reg.SetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "DelayedAutostart", RegValue.Dword(1));
        reg.SetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\disk", "Start", RegValue.Dword(0));
        var sc = new ServiceControl(reg, cmd);

        Assert.Equal(new ServiceState("DiagTrack", ServiceStart.DelayedAuto, true), sc.Query("DiagTrack"));
        Assert.Null(sc.Query("disk"));
        Assert.Null(sc.Query("bad name & del"));
        Assert.Throws<InvalidOperationException>(() => sc.SetStart("x & calc", ServiceStart.Disabled));

        sc.SetStart("DiagTrack", ServiceStart.Manual);
        Assert.Contains("sc.exe config DiagTrack start= demand", cmd.Calls);
    }

    [Fact]
    public void Toggle_is_on_and_reversible_only_after_our_own_change()
    {
        var store = _exec.Store;
        var tweak = new SampleTweak();

        var before = TweakEngine.Evaluate(tweak, Ctx(), store);
        Assert.False(before.IsOn);
        Assert.True(before.CanTurnOn);
        Assert.Equal(TweakState.NotApplied, tweak.DetectState(Ctx()));

        _exec.Apply(tweak.Id, tweak.Title, before.Plan.Actions, false);
        var after = TweakEngine.Evaluate(tweak, Ctx(), store);
        Assert.True(after.IsOn);
        Assert.True(after.CanTurnOff);
        Assert.Equal(TweakState.Applied, tweak.DetectState(Ctx()));

        foreach (var set in after.OwnChanges)
        {
            _exec.Rollback(set.Id);
        }

        Assert.False(TweakEngine.Evaluate(tweak, Ctx(), store).IsOn);
        Assert.Null(_reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A"));
    }

    [Fact]
    public void Toggle_set_by_windows_itself_cannot_be_turned_off()
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1));
        var t = TweakEngine.Evaluate(new SampleTweak(), Ctx(), _exec.Store);

        Assert.True(t.IsOn);
        Assert.False(t.CanToggle);
        Assert.Contains("уже настроено", t.Note);
    }
}
