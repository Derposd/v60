using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class TweakTests
{
    private const string Exe = @"D:\SteamLibrary\steamapps\common\dota 2 beta\game\bin\win64\dota2.exe";
    private const string Video = @"D:\SteamLibrary\steamapps\common\dota 2 beta\game\dota\cfg\video.txt";
    private const string LocalCfg = @"C:\Steam\userdata\123\config\localconfig.vdf";

    private readonly InMemoryFileSystem _fs = new();
    private readonly InMemoryRegistry _reg = new();

    private static GpuInfo Nvidia => new() { Name = "NVIDIA GeForce RTX 4060 Laptop GPU", Vendor = GpuVendor.Nvidia, VendorId = 0x10DE, DedicatedVideoMemory = 8UL << 30 };
    private static GpuInfo IntelIgpu => new() { Name = "Intel(R) UHD Graphics", Vendor = GpuVendor.Intel, VendorId = 0x8086, DedicatedVideoMemory = 128UL << 20, IsIntegrated = true };

    private TweakContext Context(SystemSnapshot? s = null, GameOptions? game = null) => new()
    {
        Snapshot = s ?? Snapshot(),
        Registry = _reg,
        FileSystem = _fs,
        Commands = new ProcessCommandRunner(),
        Game = game ?? new GameOptions(),
    };

    private static SystemSnapshot Snapshot(bool steamRunning = false, bool dotaRunning = false, bool hybrid = true) => new()
    {
        Gpus = hybrid ? new[] { IntelIgpu, Nvidia } : new[] { Nvidia },
        Profile = PcProfile.Medium,
        IsLaptop = true,
        Display = new DisplayInfo(1920, 1080, 144, "\\\\.\\DISPLAY1"),
        PowerPlan = new PowerPlanInfo(PowerPlanTweak.Balanced, "Сбалансированная", new[] { (PowerPlanTweak.Balanced, "Сбалансированная"), (PowerPlanTweak.HighPerformance, "Высокая производительность") }),
        Steam = new SteamInfo
        {
            SteamPath = @"C:\Steam",
            DotaInstallDir = @"D:\SteamLibrary\steamapps\common\dota 2 beta",
            DotaExe = Exe,
            VideoConfigPaths = new[] { Video },
            LocalConfigPath = LocalCfg,
            SteamRunning = steamRunning,
            DotaRunning = dotaRunning,
        },
    };

    [Fact]
    public void Gpu_preference_merges_with_existing_flags_and_rolls_back()
    {
        _reg.SetValue(RegHive.CurrentUser, GpuPreferenceTweak.Key, Exe, RegValue.Str("AutoHDREnable=0;"));
        var plan = new GpuPreferenceTweak().Plan(Context());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.True(plan.DefaultSelected);

        var exec = new ChangeExecutor(_fs, _reg, new ProcessCommandRunner(), new BackupStore(_fs, @"C:\b"));
        var r = exec.Apply(plan.Tweak.Id, plan.Tweak.Title, plan.Actions, false);
        Assert.Equal("AutoHDREnable=0;GpuPreference=2;", _reg.GetValue(RegHive.CurrentUser, GpuPreferenceTweak.Key, Exe)!.Data);

        exec.Rollback(r.ChangeSetId!);
        Assert.Equal("AutoHDREnable=0;", _reg.GetValue(RegHive.CurrentUser, GpuPreferenceTweak.Key, Exe)!.Data);
    }

    [Fact]
    public void Gpu_preference_not_applicable_with_single_gpu()
    {
        var plan = new GpuPreferenceTweak().Plan(Context(Snapshot(hybrid: false)));
        Assert.Equal(TweakStatus.NotApplicable, plan.Status);
        Assert.False(plan.DefaultSelected);
    }

    [Fact]
    public void Video_preset_blocked_while_game_runs()
    {
        _fs.WriteAllText(Video, VideoConfigTests.Sample);
        var plan = new VideoPresetTweak().Plan(Context(Snapshot(dotaRunning: true)));
        Assert.Equal(TweakStatus.Blocked, plan.Status);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Video_preset_plans_file_write()
    {
        _fs.WriteAllText(Video, VideoConfigTests.Sample);
        var plan = new VideoPresetTweak().Plan(Context(game: new GameOptions { Preset = VideoPreset.MaxFps }));
        Assert.Equal(TweakStatus.CanApply, plan.Status);
        var action = Assert.Single(plan.Actions);
        Assert.Equal(ActionKind.WriteFile, action.Kind);
        Assert.Contains("\"setting.r_ssao\"\t\t\"0\"", action.Content);
    }

    [Fact]
    public void Launch_options_blocked_while_steam_runs_but_line_is_offered()
    {
        _fs.WriteAllText(LocalCfg, "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"570\" { \"LaunchOptions\" \"-threads 4\" } } } } } }");
        var game = new GameOptions { FpsMax = 144 };
        game.LaunchTokens.Add("-novid");
        game.LaunchTokens.Add("+fps_max");

        var plan = new LaunchOptionsTweak().Plan(Context(Snapshot(steamRunning: true), game));

        Assert.Equal(TweakStatus.Blocked, plan.Status);
        Assert.Equal("-novid +fps_max 144", plan.CopyText);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Launch_options_written_when_steam_closed()
    {
        _fs.WriteAllText(LocalCfg, "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"570\" { \"LaunchOptions\" \"-threads 4\" } } } } } }");
        var game = new GameOptions();
        game.LaunchTokens.Add("-novid");

        var plan = new LaunchOptionsTweak().Plan(Context(game: game));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.Equal("-novid", LocalConfigEditor.GetLaunchOptions(plan.Actions.Single().Content!));
    }

    [Fact]
    public void Power_plan_has_exact_undo()
    {
        var plan = new PowerPlanTweak().Plan(Context());
        var a = Assert.Single(plan.Actions);
        Assert.Contains(PowerPlanTweak.HighPerformance.ToString(), a.Arguments);
        Assert.Contains(PowerPlanTweak.Balanced.ToString(), a.UndoArguments);
        Assert.True(plan.RecommendedHere); // laptop
    }

    [Fact]
    public void Power_plan_manual_when_high_performance_missing()
    {
        var s = Snapshot() with { PowerPlan = new PowerPlanInfo(PowerPlanTweak.Balanced, "Balanced", new[] { (PowerPlanTweak.Balanced, "Balanced") }) };
        Assert.Equal(TweakStatus.ManualOnly, new PowerPlanTweak().Plan(Context(s)).Status);
    }

    [Fact]
    public void Disputed_tweaks_are_never_selected_by_default()
    {
        var plans = TweakCatalog.PlanAll(Context());
        foreach (var p in plans.Where(p => p.Tweak.Disputed))
        {
            Assert.False(p.DefaultSelected, p.Tweak.Id);
        }

        Assert.False(plans.Single(p => p.Tweak.Id == "win.hags").DefaultSelected);
    }

    [Fact]
    public void Game_dvr_only_when_background_recording_is_on()
    {
        Assert.Equal(TweakStatus.AlreadyOptimal, new GameDvrTweak().Plan(Context()).Status);
        _reg.SetValue(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", RegValue.Dword(1));
        Assert.Equal(TweakStatus.CanApply, new GameDvrTweak().Plan(Context()).Status);
    }

    [Fact]
    public void Hags_and_priority_need_admin()
    {
        var plans = TweakCatalog.PlanAll(Context());
        Assert.True(plans.Single(p => p.Tweak.Id == "win.hags").RequiresAdmin);
        Assert.True(plans.Single(p => p.Tweak.Id == "win.priority").RequiresAdmin);
        Assert.False(plans.Single(p => p.Tweak.Id == "win.gpupreference").RequiresAdmin);
    }

    [Fact]
    public void Default_game_options_keep_existing_api_choice()
    {
        var o = TweakCatalog.DefaultGameOptions(Snapshot(), "-vulkan +fps_max 200 -language russian");
        Assert.Contains("-vulkan", o.LaunchTokens);
        Assert.Contains("-novid", o.LaunchTokens);
        Assert.Equal(200, o.FpsMax);
    }

    [Theory]
    [InlineData(null, "GpuPreference=2;")]
    [InlineData("GpuPreference=1;", "GpuPreference=2;")]
    [InlineData("SwapEffectUpgradeEnable=1;GpuPreference=0;", "SwapEffectUpgradeEnable=1;GpuPreference=2;")]
    public void Semicolon_pairs(string? existing, string expected)
    {
        Assert.Equal(expected, RegistryStrings.SetSemicolonPair(existing, "GpuPreference", "2"));
    }

    [Theory]
    [InlineData(null, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ RUNASADMIN", "~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    public void Layer_flags(string? existing, string expected)
    {
        Assert.Equal(expected, RegistryStrings.AddLayerFlag(existing, "DISABLEDXMAXIMIZEDWINDOWEDMODE"));
    }
}
