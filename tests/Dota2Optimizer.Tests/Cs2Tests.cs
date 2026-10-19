using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Crash;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class Cs2Tests
{
    private const string Steam = @"c:\program files (x86)\steam";
    private const string Install = @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive";
    private const string Video = Steam + @"\userdata\39734272\730\local\cfg\cs2_video.txt";
    private const string LocalCfg = Steam + @"\userdata\39734272\config\localconfig.vdf";

    private const string VideoText =
        "\"video.cfg\"\n{\n" +
        "\t\"Version\"\t\t\"15\"\n" +
        "\t\"setting.defaultres\"\t\t\"1920\"\n" +
        "\t\"setting.fullscreen\"\t\t\"0\"\n" +
        "\t\"setting.nowindowborder\"\t\t\"1\"\n" +
        "\t\"setting.mat_vsync\"\t\t\"1\"\n" +
        "\t\"setting.shaderquality\"\t\t\"1\"\n" +
        "\t\"setting.msaa_samples\"\t\t\"8\"\n" +
        "\t\"setting.r_csgo_cmaa_enable\"\t\t\"0\"\n" +
        "\t\"setting.videocfg_shadow_quality\"\t\t\"3\"\n" +
        "\t\"setting.videocfg_dynamic_shadows\"\t\t\"1\"\n" +
        "\t\"setting.videocfg_texture_detail\"\t\t\"2\"\n" +
        "\t\"setting.videocfg_ao_detail\"\t\t\"2\"\n" +
        "}\n";

    private readonly InMemoryFileSystem _fs = new();
    private readonly InMemoryRegistry _reg = new();

    private SteamInfo Locate(bool steamRunning = false)
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", RegValue.Str("c:/program files (x86)/steam"));
        _fs.WriteAllText(Steam + @"\steamapps\libraryfolders.vdf",
            "\"libraryfolders\" { \"0\" { \"path\" \"c:\\\\program files (x86)\\\\steam\" } \"1\" { \"path\" \"D:\\\\SteamLibrary\" } }");
        _fs.WriteAllText(@"D:\SteamLibrary\steamapps\appmanifest_730.acf",
            "\"AppState\" { \"appid\" \"730\" \"installdir\" \"Counter-Strike Global Offensive\" \"buildid\" \"777\" }");
        _fs.WriteAllText(Install + @"\game\csgo\cfg\autoexec.cfg", "bind f \"+lookatweapon\"");
        _fs.WriteAllText(Steam + @"\config\loginusers.vdf", "\"users\" { \"76561198000000000\" { \"MostRecent\" \"1\" } }");
        _fs.WriteAllText(LocalCfg,
            "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"730\" { \"LaunchOptions\" \"-novid -tickrate 128 +fps_max 400 -language english\" } } } } } }");
        _fs.WriteAllText(Video, VideoText);
        return new SteamLocator(_fs, _reg).Locate(Games.Cs2, name => steamRunning && name == "steam");
    }

    private TweakContext Context(SteamInfo steam, GameOptions? options = null) => new()
    {
        Snapshot = new SystemSnapshot { Games = TestGames.One(steam), Profile = PcProfile.Medium, Gpus = new[] { new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = GpuVendor.Nvidia, DedicatedVideoMemory = 12UL << 30 } } },
        Registry = _reg,
        FileSystem = _fs,
        Commands = new ProcessCommandRunner(),
        Game = options ?? new GameOptions(),
    };

    [Fact]
    public void Locator_finds_cs2_exe_cfg_and_userdata_video_config()
    {
        var info = Locate();

        Assert.Equal(GameId.Cs2, info.Game);
        Assert.True(info.GameFound);
        Assert.Equal("777", info.GameBuildId);
        Assert.EndsWith(@"game\bin\win64\cs2.exe", info.GameExe);
        Assert.Contains("csgo", info.GameCfgDir);
        Assert.EndsWith("cfg", info.GameCfgDir);
        var video = Assert.Single(info.VideoConfigPaths);
        Assert.Equal(Video, video.Replace('/', '\\'));
    }

    [Fact]
    public void Max_fps_preset_keeps_dynamic_shadows_and_changes_only_existing_keys()
    {
        var settings = Games.Cs2.BuildPreset(VideoPreset.MaxFps, PcProfile.Medium, 8);
        var result = VideoPresets.Apply(VideoText, settings);
        var cfg = VideoConfig.Parse(result.NewText);

        Assert.Equal("1", cfg.Get("setting.videocfg_dynamic_shadows"));
        Assert.Equal("0", cfg.Get("setting.mat_vsync"));
        Assert.Equal("0", cfg.Get("setting.videocfg_shadow_quality"));
        Assert.Equal("0", cfg.Get("setting.videocfg_ao_detail"));
        Assert.Equal("2", cfg.Get("setting.msaa_samples"));
        Assert.Equal("1920", cfg.Get("setting.defaultres"));
        Assert.Contains("setting.videocfg_particle_detail", result.MissingKeys);
        Assert.DoesNotContain("setting.videocfg_particle_detail", result.NewText);
        foreach (var preset in Enum.GetValues<VideoPreset>())
        {
            Assert.Equal("1", Games.Cs2.BuildPreset(preset, PcProfile.Weak, 2)["setting.videocfg_dynamic_shadows"]);
        }
    }

    [Fact]
    public void Launch_options_drop_csgo_myths_and_keep_user_tokens()
    {
        var analysis = LaunchOptionsComposer.Analyze("-novid -tickrate 128 +fps_max 400 -language english", Games.Cs2.Launch);
        Assert.Contains(analysis.MythTokens, m => m.Token == "-novid");
        Assert.Contains(analysis.MythTokens, m => m.Token == "-tickrate 128");
        Assert.Equal(new[] { "+fps_max 400" }, analysis.ManagedTokens);
        Assert.Equal(new[] { "-language", "english" }, analysis.UserTokens);

        var line = LaunchOptionsComposer.Compose("-novid -tickrate 128 -language english", new[] { "+fps_max", "+exec autoexec" }, 300, removeMyths: true, Games.Cs2.Launch);
        Assert.Equal("+fps_max 300 +exec autoexec -language english", line);
    }

    [Fact]
    public void Launch_tweak_writes_app_730_and_leaves_dota_alone()
    {
        var steam = Locate();
        _fs.WriteAllText(LocalCfg,
            "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"570\" { \"LaunchOptions\" \"-novid\" } \"730\" { \"LaunchOptions\" \"-threads 8\" } } } } } }");
        var options = new GameOptions { FpsMax = 240 };
        options.LaunchTokens.Add("+fps_max");

        var plan = new LaunchOptionsTweak(Games.Cs2).Plan(Context(steam, options));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        var written = plan.Actions.Single().Content!;
        Assert.Equal("+fps_max 240", LocalConfigEditor.GetLaunchOptions(written, "730"));
        Assert.Equal("-novid", LocalConfigEditor.GetLaunchOptions(written, "570"));
    }

    [Fact]
    public void Switch_api_toggles_vulkan_without_a_dx11_flag()
    {
        Assert.Equal("-vulkan", QuickFixes.SwitchApi(null, Games.Cs2.Launch));
        Assert.Equal("+fps_max 300 -language english", QuickFixes.SwitchApi("-vulkan +fps_max 300 -language english", Games.Cs2.Launch));
        Assert.Equal("-dx11", QuickFixes.SwitchApi("-vulkan", Games.Dota2.Launch));
    }

    [Fact]
    public void Fixes_are_journaled_under_cs2_ids_and_undoable()
    {
        var steam = Locate();
        var exec = new ChangeExecutor(_fs, _reg, new ProcessCommandRunner(), new BackupStore(_fs, @"C:\b"));

        var reset = exec.Apply(QuickFixes.ResetVideoId(Games.Cs2), "Сброс", QuickFixes.PlanResetVideo(Games.Cs2, steam), false);
        Assert.True(reset.Success);
        Assert.False(_fs.FileExists(Video));
        Assert.Equal("cs2.fix.reset-video", exec.Store.Active().Single().TweakId);
        Assert.False(exec.Store.Active().Single().TweakId.StartsWith(Games.Dota2.FixPrefix, StringComparison.Ordinal));

        exec.Rollback(reset.ChangeSetId!);
        Assert.Equal(VideoText, _fs.ReadAllText(Video));

        var autoexec = QuickFixes.PlanDisableAutoexec(_fs, steam);
        Assert.Equal(2, autoexec.Count);
    }

    [Fact]
    public void Fullscreen_tweak_only_sets_the_fullscreen_key_in_cs2()
    {
        var steam = Locate();
        var plan = new ExclusiveFullscreenTweak(Games.Cs2).Plan(Context(steam));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        var cfg = VideoConfig.Parse(plan.Actions.Single().Content!);
        Assert.Equal("1", cfg.Get("setting.fullscreen"));
        Assert.Equal("1", cfg.Get("setting.nowindowborder"));
    }

    [Fact]
    public void Tweak_ids_are_per_game_and_dota_ids_are_unchanged()
    {
        var dota = TweakCatalog.For(Games.Dota2).Select(t => t.Id).ToList();
        var cs2 = TweakCatalog.For(Games.Cs2).Select(t => t.Id).ToList();

        Assert.Contains("dota.video.preset", dota);
        Assert.Contains("win.gpupreference", dota);
        Assert.Contains("cs2.video.preset", cs2);
        Assert.Contains("win.gpupreference.cs2", cs2);
        Assert.Contains("win.powerplan", cs2);
        Assert.Equal(cs2.Count, cs2.Distinct().Count());
        Assert.Contains(TweakCatalog.For(Games.Cs2), t => t.Title.Contains("CS2", StringComparison.Ordinal));
        Assert.DoesNotContain(TweakCatalog.For(Games.Cs2).OfType<GameTweak>(), t => t.Title.Contains("Dota", StringComparison.Ordinal) || t.Explanation.Contains("Dota", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_options_favour_fps_and_never_add_novid()
    {
        var snapshot = new SystemSnapshot { Profile = PcProfile.Medium, Display = new DisplayInfo(1920, 1080, 240, "x") };
        var options = TweakCatalog.DefaultGameOptions(Games.Cs2, snapshot, "-novid +fps_max 400");

        Assert.Equal(VideoPreset.MaxFps, options.Preset);
        Assert.DoesNotContain("-novid", options.LaunchTokens);
        Assert.Contains("+fps_max", options.LaunchTokens);
        Assert.Equal(400, options.FpsMax);
    }

    [Fact]
    public void Issues_have_steps_and_texts_mention_cs2_not_dota()
    {
        Assert.NotEmpty(Games.Cs2.Issues);
        Assert.Equal(Games.Cs2.Issues.Count, Games.Cs2.Issues.Select(i => i.Id).Distinct().Count());
        foreach (var i in Games.Cs2.Issues)
        {
            Assert.NotEmpty(i.Steps);
            Assert.DoesNotContain("Dota", i.Symptom + i.Cause + string.Join(' ', i.Steps), StringComparison.Ordinal);
        }

        Assert.Contains(Games.Cs2.Issues, i => i.Fixes.Contains(QuickFix.VerifyFiles));
        Assert.Contains(Games.Cs2.Issues, i => i.Fixes.Contains(QuickFix.ResetVideoSettings));
    }

    [Fact]
    public void Crash_classifier_reads_cs2_events_only_for_cs2()
    {
        var events = new[]
        {
            new RawEvent(new DateTime(2026, 10, 1), "Application", "Application Error", 1000, "cs2.exe crashed",
                new[] { "cs2.exe", "1.0", "0", "rendersystemdx11.dll", "1", "0", "c0000005" }),
            new RawEvent(new DateTime(2026, 10, 2), "Application", "Application Error", 1000, "dota2.exe crashed",
                new[] { "dota2.exe", "1.0", "0", "nvwgf2umx.dll", "32.0", "0", "c0000005" }),
        };

        var cs2 = CrashClassifier.Classify(events, Games.Cs2);
        var finding = Assert.Single(cs2);
        Assert.Contains("Counter-Strike 2", finding.Title);
        Assert.Equal("Рендер", finding.Category);

        Assert.Single(CrashClassifier.Classify(events, Games.Dota2));
    }
}
