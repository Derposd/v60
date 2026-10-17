using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class DotaFixesTests
{
    private const string Cfg = @"D:\Dota\game\dota\cfg";
    private const string Video = Cfg + @"\video.txt";
    private const string Cloud = @"C:\Steam\userdata\1\570\remote\cfg\video.txt";
    private const string LocalCfg = @"C:\Steam\userdata\1\config\localconfig.vdf";

    private readonly InMemoryFileSystem _fs = new();
    private readonly InMemoryRegistry _reg = new();

    private ChangeExecutor Exec() => new(_fs, _reg, new ProcessCommandRunner(), new BackupStore(_fs, @"C:\b"));

    private static SteamInfo Steam(bool steamRunning = false) => new()
    {
        SteamPath = @"C:\Steam",
        DotaInstallDir = @"D:\Dota",
        DotaCfgDir = Cfg,
        VideoConfigPaths = new[] { Video, Cloud },
        LocalConfigPath = LocalCfg,
        SteamRunning = steamRunning,
    };

    [Fact]
    public void Reset_video_deletes_all_copies_and_undo_restores_them()
    {
        _fs.WriteAllText(Video, "a");
        _fs.WriteAllText(Cloud, "b");
        var exec = Exec();

        var r = exec.Apply(DotaQuickFixes.ResetVideoId, "Сброс", DotaQuickFixes.PlanResetVideo(Steam()), false);
        Assert.False(_fs.FileExists(Video));
        Assert.False(_fs.FileExists(Cloud));

        exec.Rollback(r.ChangeSetId!);
        Assert.Equal("a", _fs.ReadAllText(Video));
        Assert.Equal("b", _fs.ReadAllText(Cloud));
    }

    [Fact]
    public void Disable_autoexec_renames_and_undo_restores()
    {
        _fs.WriteAllText(Cfg + @"\autoexec.cfg", "fps_max 144");
        var exec = Exec();

        var r = exec.Apply(DotaQuickFixes.DisableAutoexecId, "autoexec", DotaQuickFixes.PlanDisableAutoexec(_fs, Steam()), false);
        Assert.False(_fs.FileExists(Cfg + @"\autoexec.cfg"));
        Assert.Equal("fps_max 144", _fs.ReadAllText(Cfg + @"\autoexec.cfg.disabled"));

        exec.Rollback(r.ChangeSetId!);
        Assert.Equal("fps_max 144", _fs.ReadAllText(Cfg + @"\autoexec.cfg"));
        Assert.False(_fs.FileExists(Cfg + @"\autoexec.cfg.disabled"));
    }

    [Fact]
    public void Disable_autoexec_without_file_plans_nothing()
    {
        Assert.Empty(DotaQuickFixes.PlanDisableAutoexec(_fs, Steam()));
    }

    [Theory]
    [InlineData(null, "-vulkan")]
    [InlineData("-novid", "-novid -vulkan")]
    [InlineData("-vulkan -novid -language russian", "-novid -dx11 -language russian")]
    [InlineData("-dx11 +fps_max 144", "+fps_max 144 -vulkan")]
    public void Switch_api_swaps_and_keeps_the_rest(string? current, string expected)
    {
        Assert.Equal(expected, DotaQuickFixes.SwitchApi(current));
    }

    [Fact]
    public void Switch_api_blocked_while_steam_runs_but_offers_line()
    {
        _fs.WriteAllText(LocalCfg, "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"570\" { \"LaunchOptions\" \"-dx11\" } } } } } }");
        var (actions, line, blocker) = DotaQuickFixes.PlanSwitchApi(_fs, Steam(steamRunning: true));
        Assert.Empty(actions);
        Assert.Equal("-vulkan", line);
        Assert.NotNull(blocker);

        var (actions2, _, blocker2) = DotaQuickFixes.PlanSwitchApi(_fs, Steam());
        Assert.Null(blocker2);
        Assert.Equal("-vulkan", LocalConfigEditor.GetLaunchOptions(Assert.Single(actions2).Content!));
    }

    [Fact]
    public void Map_preload_is_a_managed_disputed_option()
    {
        var a = LaunchOptionsComposer.Analyze("-map dota -novid");
        Assert.Contains("-map dota", a.ManagedTokens);
        Assert.Empty(a.MythTokens);
        Assert.True(LaunchOptionCatalog.MapPreload.Disputed);
        Assert.Equal("-novid", LaunchOptionsComposer.Compose("-map dota", new[] { "-novid" }, null, true));
        Assert.Equal("-novid -map dota", LaunchOptionsComposer.Compose(null, new[] { "-novid", "-map dota" }, null, true));
    }

    [Fact]
    public void Exclusive_fullscreen_tweak_only_changes_mode_keys()
    {
        const string text = "\"config\"\n{\n\t\"setting.fullscreen\"\t\t\"0\"\n\t\"setting.nowindowborder\"\t\t\"1\"\n\t\"setting.r_ssao\"\t\t\"1\"\n}\n";
        _fs.WriteAllText(Video, text);
        var ctx = new TweakContext
        {
            Snapshot = new SystemSnapshot { Steam = Steam() with { VideoConfigPaths = new[] { Video } } },
            Registry = _reg,
            FileSystem = _fs,
            Commands = new ProcessCommandRunner(),
        };

        Assert.True(ExclusiveFullscreenTweak.IsWindowed(ctx));
        var plan = new ExclusiveFullscreenTweak().Plan(ctx);
        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.False(plan.DefaultSelected);
        Assert.Equal(text.Replace("\"0\"", "\"1\"").Replace("nowindowborder\"\t\t\"1\"", "nowindowborder\"\t\t\"0\""), plan.Actions.Single().Content);
    }

    [Fact]
    public void Windowed_optimizations_toggle_preserves_other_global_flags()
    {
        _reg.SetValue(RegHive.CurrentUser, GpuPreferenceTweak.Key, "DirectXUserGlobalSettings", RegValue.Str("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;"));
        var ctx = new TweakContext
        {
            Snapshot = new SystemSnapshot { Windows = new WindowsInfo("Windows 11", "24H2", 26100, true) },
            Registry = _reg,
            FileSystem = _fs,
            Commands = new ProcessCommandRunner(),
        };

        var plan = new WindowedGameOptimizationsTweak().Plan(ctx);

        Assert.True(plan.Tweak.Disputed);
        Assert.False(plan.DefaultSelected);
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;", plan.Actions.Single().Value!.Data);

        var win10 = new TweakContext { Snapshot = new SystemSnapshot { Windows = new WindowsInfo("Windows 10", "22H2", 19045, false) }, Registry = _reg, FileSystem = _fs, Commands = new ProcessCommandRunner() };
        Assert.Equal(TweakStatus.NotApplicable, new WindowedGameOptimizationsTweak().Plan(win10).Status);
    }

    [Fact]
    public void Every_known_issue_has_steps_and_evidence()
    {
        foreach (var i in KnownIssues.All)
        {
            Assert.NotEmpty(i.Steps);
            Assert.False(string.IsNullOrWhiteSpace(i.Evidence), i.Id);
        }

        Assert.Contains(KnownIssues.All, i => i.Fixes.Contains(QuickFix.StartMatchMonitor));
    }

    // ---------- match monitor analysis ----------

    private static List<MinuteFps> Minutes(params double[] fps) =>
        fps.Select((f, i) => new MinuteFps(i, f, f * 0.6, 3000)).ToList();

    private static List<MatchSample> Samples(int n, Func<int, MatchSample> f) => Enumerable.Range(0, n).Select(f).ToList();

    [Fact]
    public void Per_minute_fps_groups_frames()
    {
        var frames = Enumerable.Range(0, 600).Select(i => (i * 0.1, 10.0))       // minute 0: 100 fps
            .Concat(Enumerable.Range(0, 300).Select(i => (60 + i * 0.2, 20.0))) // minute 1: 50 fps
            .ToList();
        var m = MatchAnalyzer.FpsPerMinute(frames);
        Assert.Equal(2, m.Count);
        Assert.Equal(100, m[0].AvgFps, 3);
        Assert.Equal(50, m[1].AvgFps, 3);
    }

    [Fact]
    public void Timed_frames_from_presentmon_v2_csv()
    {
        var csv = "Application,CPUStartTime,MsBetweenPresents\ndota2.exe,1000.0,7.0\ndwm.exe,1001.0,16.6\ndota2.exe,61000.5,8.0\n";
        var frames = PresentMonCsv.ReadTimedFrames(csv);
        Assert.Equal(new[] { (1.0, 7.0), (61.0005, 8.0) }, frames);
    }

    [Fact]
    public void Detects_thermal_throttling_late_in_match()
    {
        var samples = Samples(40, i => new MatchSample(i * 60, 2000, 2500, 6000, i < 20 ? 110 : 75, 60, 80, 2000));
        var minutes = Minutes(Enumerable.Range(0, 40).Select(i => i < 20 ? 140.0 : 95.0).ToArray());

        var r = MatchAnalyzer.Analyze(samples, minutes, 8192, 16384);

        Assert.True(r.DropPercent > 25);
        Assert.Contains(r.Findings, f => f.Title.Contains("частоту") && f.Severity == Severity.Critical);
    }

    [Fact]
    public void Detects_vram_pressure_and_memory_growth()
    {
        var samples = Samples(40, i => new MatchSample(i * 60, 2000 + i * 100, 2000 + i * 120, 4000, 100, 50, 85, 3500 + i * 15));
        var r = MatchAnalyzer.Analyze(samples, Minutes(Enumerable.Repeat(120.0, 40).ToArray()), 4096, 16384);

        Assert.Contains(r.Findings, f => f.Title.Contains("Видеопамять"));
        Assert.Contains(r.Findings, f => f.Title.Contains("памяти игрой"));
        Assert.Contains("стабилен", r.Summary);
    }

    [Fact]
    public void Cpu_bound_late_fights_are_explained()
    {
        var samples = Samples(30, i => new MatchSample(i * 60, 2000, 2500, 6000, 100, 70, i < 15 ? 80 : 55, 2000));
        var r = MatchAnalyzer.Analyze(samples, Minutes(Enumerable.Range(0, 30).Select(i => i < 15 ? 160.0 : 110.0).ToArray()), 8192, 32768);
        Assert.Contains(r.Findings, f => f.Title.Contains("процессор"));
    }

    [Fact]
    public void Without_fps_the_report_says_so()
    {
        var r = MatchAnalyzer.Analyze(Samples(10, i => new MatchSample(i * 60, 2000, 2500, 6000, 100, 50, 80, 2000)), Array.Empty<MinuteFps>(), 8192, 16384);
        Assert.Null(r.DropPercent);
        Assert.Contains("FPS не записан", r.Summary);
        Assert.Contains(r.Findings, f => f.Title == "Профилактика");
    }
}
