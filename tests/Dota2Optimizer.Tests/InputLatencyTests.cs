using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class InputLatencyTests
{
    private const string VideoPath = @"c:\steam\userdata\1\730\local\cfg\cs2_video.txt";

    private readonly InMemoryRegistry _reg = new();
    private readonly InMemoryFileSystem _fs = new();

    private TweakContext Ctx(SystemSnapshot? s = null) => new()
    {
        Snapshot = s ?? new SystemSnapshot(),
        Registry = _reg,
        FileSystem = _fs,
        Commands = new ProcessCommandRunner(),
    };

    private static SystemSnapshot Snap(int hz, int maxHz, GpuVendor vendor = GpuVendor.Nvidia) => new()
    {
        Display = new DisplayInfo(1920, 1080, hz, "DISPLAY1") { MaxRefreshHz = maxHz },
        Gpus = new[] { new GpuInfo { Name = "Test GPU", Vendor = vendor, DedicatedVideoMemory = 8UL << 30 } },
    };

    [Fact]
    public void Refresh_rate_below_the_monitor_maximum_is_flagged_but_never_changed_by_the_app()
    {
        var plan = new RefreshRateTweak().Plan(Ctx(Snap(60, 165)));

        Assert.Equal(TweakStatus.ManualOnly, plan.Status);
        Assert.Empty(plan.Actions);
        Assert.Contains("165", plan.StatusText);
        Assert.Equal(Effectiveness.High, plan.Tweak.Effectiveness);
    }

    [Fact]
    public void Refresh_rate_at_maximum_or_unknown_needs_nothing()
    {
        Assert.Equal(TweakStatus.AlreadyOptimal, new RefreshRateTweak().Plan(Ctx(Snap(165, 165))).Status);
        Assert.Equal(TweakStatus.AlreadyOptimal, new RefreshRateTweak().Plan(Ctx(Snap(165, 166))).Status);
        Assert.Equal(TweakStatus.NotApplicable, new RefreshRateTweak().Plan(Ctx(Snap(60, 0))).Status);
        Assert.Equal(TweakStatus.NotApplicable, new RefreshRateTweak().Plan(Ctx()).Status);
    }

    [Fact]
    public void Low_latency_hint_depends_on_the_video_card_vendor()
    {
        var tweak = new LowLatencyModeTweak(Games.Cs2);

        var nvidia = tweak.Plan(Ctx(Snap(144, 144, GpuVendor.Nvidia)));
        Assert.Equal(TweakStatus.ManualOnly, nvidia.Status);
        Assert.Contains("Reflex", nvidia.StatusText);

        var amd = tweak.Plan(Ctx(Snap(144, 144, GpuVendor.Amd)));
        Assert.Equal(TweakStatus.ManualOnly, amd.Status);
        Assert.Contains("Anti-Lag", amd.StatusText);

        Assert.Equal(TweakStatus.NotApplicable, tweak.Plan(Ctx(Snap(144, 144, GpuVendor.Intel))).Status);
        Assert.DoesNotContain("Dota", tweak.Title);
    }

    [Fact]
    public void Timer_resolution_is_optional_admin_only_and_reversible()
    {
        var tweak = new GlobalTimerResolutionTweak();
        var plan = tweak.Plan(Ctx());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.False(plan.DefaultSelected);
        Assert.True(plan.RequiresAdmin);
        Assert.Single(plan.Actions);
        Assert.Equal("1", plan.Actions[0].Value!.Data);

        _reg.SetValue(RegHive.LocalMachine, GlobalTimerResolutionTweak.Key, GlobalTimerResolutionTweak.Name, RegValue.Dword(1));
        Assert.Equal(TweakStatus.AlreadyOptimal, tweak.Plan(Ctx()).Status);
    }

    [Fact]
    public void Timer_resolution_is_not_offered_on_old_windows()
    {
        var s = new SystemSnapshot { Windows = new WindowsInfo("Windows 10", "1903", 18362, false) };
        Assert.Equal(TweakStatus.NotApplicable, new GlobalTimerResolutionTweak().Plan(Ctx(s)).Status);
    }

    [Fact]
    public void Multiplane_overlay_is_a_disputed_opt_in()
    {
        var tweak = new MultiplaneOverlayTweak();
        var plan = tweak.Plan(Ctx());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.True(tweak.Disputed);
        Assert.False(plan.DefaultSelected);
        Assert.Equal("5", plan.Actions[0].Value!.Data);

        _reg.SetValue(RegHive.LocalMachine, MultiplaneOverlayTweak.Key, MultiplaneOverlayTweak.Name, RegValue.Dword(5));
        Assert.Equal(TweakStatus.AlreadyOptimal, tweak.Plan(Ctx()).Status);
    }

    [Fact]
    public void Usb_suspend_only_touches_values_that_are_still_default_and_restores_them()
    {
        var guid = Guid.NewGuid();
        var s = new SystemSnapshot { PowerPlan = new PowerPlanInfo(guid, "Высокая производительность", Array.Empty<(Guid, string)>()) };
        var key = $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{guid}\{UsbSelectiveSuspendTweak.UsbSubgroup}\{UsbSelectiveSuspendTweak.SelectiveSuspend}";
        _reg.SetValue(RegHive.LocalMachine, key, "ACSettingIndex", RegValue.Dword(0));

        var plan = new UsbSelectiveSuspendTweak().Plan(Ctx(s));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.True(plan.RequiresAdmin);
        Assert.DoesNotContain(plan.Actions, a => a.Arguments!.Contains("/setacvalueindex"));
        var dc = Assert.Single(plan.Actions, a => a.Arguments!.Contains("/setdcvalueindex"));
        Assert.EndsWith(" 0", dc.Arguments!);
        Assert.EndsWith(" 1", dc.UndoArguments!);
    }

    [Fact]
    public void Usb_suspend_already_off_in_both_modes()
    {
        var guid = Guid.NewGuid();
        var s = new SystemSnapshot { PowerPlan = new PowerPlanInfo(guid, "План", Array.Empty<(Guid, string)>()) };
        var key = $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{guid}\{UsbSelectiveSuspendTweak.UsbSubgroup}\{UsbSelectiveSuspendTweak.SelectiveSuspend}";
        _reg.SetValue(RegHive.LocalMachine, key, "ACSettingIndex", RegValue.Dword(0));
        _reg.SetValue(RegHive.LocalMachine, key, "DCSettingIndex", RegValue.Dword(0));

        Assert.Equal(TweakStatus.AlreadyOptimal, new UsbSelectiveSuspendTweak().Plan(Ctx(s)).Status);
    }

    [Fact]
    public void Catalog_registers_the_new_tweaks_for_both_games_without_wrong_game_names()
    {
        foreach (var game in new[] { Games.Dota2, Games.Cs2 })
        {
            var tweaks = TweakCatalog.For(game);
            foreach (var id in new[] { "win.refreshrate", "win.timerres", "win.mpo", "win.usbsuspend", game.TweakId("lowlatency") })
            {
                Assert.Contains(tweaks, t => t.Id == id);
            }
        }

        var cs2 = TweakCatalog.For(Games.Cs2).Where(t => t.Id.StartsWith("cs2.", StringComparison.Ordinal));
        Assert.All(cs2, t => Assert.DoesNotContain("Dota", t.Title + t.Explanation));
    }

    [Fact]
    public void Advisor_reports_low_refresh_and_vsync()
    {
        var advice = InputLagAdvisor.Evaluate(Games.Cs2, Snap(60, 144), Array.Empty<TweakPlan>(), vSyncOn: true);

        Assert.Contains(advice, a => a.Title.Contains("60 Гц") && a.Severity == Severity.Warning);
        Assert.Contains(advice, a => a.Title.Contains("V-Sync"));
    }

    [Fact]
    public void Advisor_is_quiet_when_nothing_adds_delay()
    {
        var advice = InputLagAdvisor.Evaluate(Games.Cs2, Snap(144, 144), Array.Empty<TweakPlan>(), vSyncOn: false);
        Assert.Empty(advice);

        Assert.Empty(InputLagAdvisor.Evaluate(Games.Cs2, Snap(144, 144), Array.Empty<TweakPlan>(), vSyncOn: null));
    }

    [Fact]
    public void Vsync_is_read_from_the_video_config()
    {
        var steam = new SteamInfo { VideoConfigPaths = new[] { VideoPath }, GameInstallDir = @"d:\game" };
        Assert.Null(InputLagAdvisor.ReadVSync(_fs, steam));

        _fs.WriteAllText(VideoPath, "\"video.cfg\"\n{\n\t\"setting.mat_vsync\"\t\t\"1\"\n}\n");
        Assert.True(InputLagAdvisor.ReadVSync(_fs, steam));

        _fs.WriteAllText(VideoPath, "\"video.cfg\"\n{\n\t\"setting.mat_vsync\"\t\t\"0\"\n}\n");
        Assert.False(InputLagAdvisor.ReadVSync(_fs, steam));

        _fs.WriteAllText(VideoPath, "\"video.cfg\"\n{\n\t\"setting.fullscreen\"\t\t\"1\"\n}\n");
        Assert.Null(InputLagAdvisor.ReadVSync(_fs, steam));
    }
}
