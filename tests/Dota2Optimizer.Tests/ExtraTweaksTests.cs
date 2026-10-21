using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class ExtraTweaksTests
{
    private readonly InMemoryRegistry _reg = new();
    private readonly InMemoryFileSystem _fs = new();

    private TweakContext Ctx() => new()
    {
        Snapshot = new SystemSnapshot(),
        Registry = _reg,
        FileSystem = _fs,
        Commands = new ProcessCommandRunner(),
    };

    [Fact]
    public void Accessibility_shortcuts_are_turned_off_but_features_in_use_are_left_alone()
    {
        // Defaults: all three shortcuts on. Sticky keys itself switched on by the user.
        _reg.SetValue(RegHive.CurrentUser, @"Control Panel\Accessibility\StickyKeys", "Flags", RegValue.Str("511"));

        var plan = new AccessibilityHotkeysTweak().Plan(Ctx());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.True(plan.DefaultSelected);
        Assert.Equal(2, plan.Actions.Count);
        Assert.DoesNotContain(plan.Actions, a => a.Key!.EndsWith("StickyKeys"));
        Assert.Contains(plan.Actions, a => a.Key!.EndsWith("Keyboard Response") && a.Value!.Data == "122");
        Assert.Contains(plan.Actions, a => a.Key!.EndsWith("ToggleKeys") && a.Value!.Data == "58");
        Assert.Contains("залипание", plan.StatusText);
    }

    [Fact]
    public void Accessibility_shortcuts_already_off()
    {
        foreach (var (key, _, def) in AccessibilityHotkeysTweak.Features)
        {
            _reg.SetValue(RegHive.CurrentUser, key, "Flags", RegValue.Str((def & ~AccessibilityHotkeysTweak.HotkeyActive).ToString()));
        }

        Assert.Equal(TweakStatus.AlreadyOptimal, new AccessibilityHotkeysTweak().Plan(Ctx()).Status);
    }

    [Fact]
    public void Mouse_acceleration_is_optional_and_shared_between_games()
    {
        var dota = new MouseAccelerationTweak(Games.Dota2);
        var cs2 = new MouseAccelerationTweak(Games.Cs2);
        Assert.Equal(dota.Id, cs2.Id);
        Assert.Equal(Effectiveness.Medium, dota.Effectiveness);
        Assert.Equal(Effectiveness.Low, cs2.Effectiveness);

        var plan = dota.Plan(Ctx());
        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.False(plan.DefaultSelected);
        Assert.All(plan.Actions, a => Assert.Equal("0", a.Value!.Data));

        _reg.SetValue(RegHive.CurrentUser, MouseAccelerationTweak.Key, "MouseSpeed", RegValue.Str("0"));
        Assert.Equal(TweakStatus.AlreadyOptimal, dota.Plan(Ctx()).Status);
    }

    [Fact]
    public void Disputed_system_tweaks_need_admin_and_are_never_preselected()
    {
        foreach (var t in new Tweak[] { new PowerThrottlingTweak(), new MmcssGamesTweak(), new DeliveryOptimizationTweak() })
        {
            var plan = t.Plan(Ctx());
            Assert.Equal(TweakStatus.CanApply, plan.Status);
            Assert.False(plan.DefaultSelected, t.Id);
            Assert.True(plan.RequiresAdmin, t.Id);
        }
    }

    [Fact]
    public void Mmcss_only_writes_values_that_differ()
    {
        const string games = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        _reg.SetValue(RegHive.LocalMachine, games, "GPU Priority", RegValue.Dword(8));
        _reg.SetValue(RegHive.LocalMachine, games, "Scheduling Category", RegValue.Str("high"));

        var plan = new MmcssGamesTweak().Plan(Ctx());

        Assert.Equal(3, plan.Actions.Count);
        Assert.DoesNotContain(plan.Actions, a => a.ValueName is "GPU Priority" or "Scheduling Category");
    }

    [Fact]
    public void Memory_integrity_is_only_ever_manual()
    {
        Assert.Equal(TweakStatus.NotApplicable, new MemoryIntegrityTweak().Plan(Ctx()).Status);
        _reg.SetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", RegValue.Dword(1));
        var plan = new MemoryIntegrityTweak().Plan(Ctx());
        Assert.Equal(TweakStatus.ManualOnly, plan.Status);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Every_game_lists_the_new_tweaks_once()
    {
        foreach (var game in Games.All)
        {
            var ids = TweakCatalog.For(game).Select(t => t.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Contains("win.stickykeys", ids);
            Assert.Contains("win.mouseaccel", ids);
            Assert.Contains("win.mmcss", ids);
        }
    }

    private sealed class FakePowercfg : ICommandRunner
    {
        public Guid Active { get; set; } = PowerPlanTweak.Balanced;
        public bool HasHighPerformance { get; set; } = true;
        public List<string> Calls { get; } = new();

        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null)
        {
            Calls.Add(arguments);
            if (arguments == "/getactivescheme")
            {
                return new CommandResult(0, $"GUID схемы питания: {Active}  (Сбалансированная)", string.Empty);
            }

            var guid = Guid.Parse(arguments.Split(' ')[1]);
            if (guid == PowerPlanTweak.HighPerformance && !HasHighPerformance)
            {
                return new CommandResult(1, string.Empty, "нет схемы");
            }

            Active = guid;
            return new CommandResult(0, string.Empty, string.Empty);
        }
    }

    [Fact]
    public void Session_boost_switches_plan_for_the_game_and_restores_it()
    {
        var cmd = new FakePowercfg();
        var boost = new GameSessionBoost(cmd, _fs, @"C:\data\session-power.txt");

        Assert.Null(boost.Update(null));
        Assert.NotNull(boost.Update("CS2"));
        Assert.Equal(PowerPlanTweak.HighPerformance, cmd.Active);
        Assert.True(boost.Active);

        Assert.Null(boost.Update("CS2"));
        var calls = cmd.Calls.Count;
        Assert.Null(boost.Update("CS2"));
        Assert.Equal(calls, cmd.Calls.Count);

        Assert.NotNull(boost.Update(null));
        Assert.Equal(PowerPlanTweak.Balanced, cmd.Active);
        Assert.False(boost.Active);
    }

    [Fact]
    public void Session_boost_leaves_a_plan_the_user_chose_during_the_game()
    {
        var cmd = new FakePowercfg();
        var boost = new GameSessionBoost(cmd, _fs, @"C:\data\session-power.txt");
        boost.Update("Dota 2");
        cmd.Active = PowerPlanTweak.UltimatePerformance;

        Assert.Null(boost.Update(null));
        Assert.Equal(PowerPlanTweak.UltimatePerformance, cmd.Active);
        Assert.False(boost.Active);
    }

    [Fact]
    public void Session_boost_does_nothing_when_already_fast_or_plan_missing()
    {
        var fast = new FakePowercfg { Active = PowerPlanTweak.HighPerformance };
        Assert.Null(new GameSessionBoost(fast, _fs, @"C:\a.txt").Update("CS2"));
        Assert.Single(fast.Calls);

        var missing = new FakePowercfg { HasHighPerformance = false };
        var boost = new GameSessionBoost(missing, _fs, @"C:\b.txt");
        Assert.Null(boost.Update("CS2"));
        Assert.False(boost.Active);
        Assert.Equal(PowerPlanTweak.Balanced, missing.Active);
    }

    [Fact]
    public void Temp_cleaner_removes_only_old_files_and_never_follows_links()
    {
        var root = Path.Combine(Path.GetTempPath(), "fl-temp-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "fl-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            Directory.CreateDirectory(outside);
            var old = Path.Combine(root, "sub", "old.tmp");
            var fresh = Path.Combine(root, "fresh.tmp");
            var keep = Path.Combine(outside, "keep.txt");
            File.WriteAllBytes(old, new byte[1000]);
            File.WriteAllBytes(fresh, new byte[500]);
            File.WriteAllBytes(keep, new byte[10]);
            var past = DateTime.UtcNow.AddDays(-3);
            File.SetCreationTimeUtc(old, past);
            File.SetLastWriteTimeUtc(old, past);
            File.SetLastWriteTimeUtc(keep, past);
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), outside);

            var measured = TempCleaner.Measure(root, DateTime.UtcNow);
            Assert.Equal(1, measured.Files);
            Assert.Equal(1000, measured.Bytes);

            var cleaned = TempCleaner.Clean(root, DateTime.UtcNow);
            Assert.Equal(1, cleaned.Files);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(keep));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }
}
