using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.GtaRp;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class GtaRpTests
{
    private const string Docs = @"C:\Users\Player\Documents";
    private const string Settings = Docs + @"\Rockstar Games\GTA V\settings.xml";
    private const string GameDir = @"D:\Games\Grand Theft Auto V";
    private const string RageDir = @"C:\RAGEMP";

    // The file as GTA V writes it (version 27), at "very high" settings on a 1080p monitor.
    private const string SettingsText =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n\n<Settings>\n  <version value=\"27\" />\n  <configSource>SMC_AUTO</configSource>\n  <graphics>\n" +
        "    <Tessellation value=\"2\" />\n    <LodScale value=\"1.000000\" />\n    <PedLodBias value=\"0.200000\" />\n    <VehicleLodBias value=\"0.200000\" />\n" +
        "    <ShadowQuality value=\"3\" />\n    <ReflectionQuality value=\"3\" />\n    <ReflectionMSAA value=\"4\" />\n    <SSAO value=\"2\" />\n" +
        "    <AnisotropicFiltering value=\"16\" />\n    <MSAA value=\"4\" />\n    <MSAAFragments value=\"0\" />\n    <MSAAQuality value=\"0\" />\n" +
        "    <SamplingMode value=\"0\" />\n    <TextureQuality value=\"2\" />\n    <ParticleQuality value=\"2\" />\n    <WaterQuality value=\"2\" />\n" +
        "    <GrassQuality value=\"3\" />\n    <ShaderQuality value=\"2\" />\n    <Shadow_SoftShadows value=\"4\" />\n    <UltraShadows_Enabled value=\"true\" />\n" +
        "    <Shadow_ParticleShadows value=\"true\" />\n    <Shadow_Distance value=\"2.000000\" />\n    <Shadow_LongShadows value=\"true\" />\n" +
        "    <Reflection_MipBlur value=\"true\" />\n    <FXAA_Enabled value=\"false\" />\n    <TXAA_Enabled value=\"false\" />\n    <Lighting_FogVolumes value=\"true\" />\n" +
        "    <PostFX value=\"3\" />\n    <DoF value=\"true\" />\n    <HdStreamingInFlight value=\"true\" />\n    <MaxLodScale value=\"1.000000\" />\n" +
        "    <MotionBlurStrength value=\"1.000000\" />\n  </graphics>\n  <video>\n    <ScreenWidth value=\"1920\" />\n    <ScreenHeight value=\"1080\" />\n" +
        "    <RefreshRate value=\"144\" />\n    <Windowed value=\"2\" />\n    <VSync value=\"1\" />\n  </video>\n  <VideoCardDescription>Test GPU</VideoCardDescription>\n</Settings>\n";

    private readonly InMemoryRegistry _reg = new();
    private readonly InMemoryFileSystem _fs = new();

    // Paths are built with Path.Combine, so on a non-Windows test host they carry "/" — compare them in Windows form.
    private static string? N(string? path) => path?.Replace('/', '\\');

    private GtaInfo Install(bool settings = true, bool rageMp = true, bool running = false)
    {
        _fs.WriteAllText(GameDir + @"\GTA5.exe", "x");
        _reg.SetValue(RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolderSteam", RegValue.Str(GameDir));
        if (settings)
        {
            _fs.WriteAllText(Settings, SettingsText);
        }

        if (rageMp)
        {
            _fs.WriteAllText(RageDir + @"\ragemp_v.exe", "x");
            _fs.WriteAllText(RageDir + @"\client_resources\server\a.dat", "cache");
        }

        return new GtaLocator(_fs, _reg, Docs).Locate(name => running && name == "GTA5");
    }

    private static SystemSnapshot Pc(PcProfile profile, ulong vramGb, bool integrated = false, int w = 1920, int h = 1080, double ramGb = 16) => new()
    {
        Profile = profile,
        Gpus = new[] { new GpuInfo { Name = "Test", Vendor = GpuVendor.Nvidia, DedicatedVideoMemory = vramGb << 30, IsIntegrated = integrated } },
        Memory = new MemoryInfo((ulong)(ramGb * 1024 * 1024 * 1024), Array.Empty<MemoryModule>()),
        Display = new DisplayInfo(w, h, 144, "D") { MaxRefreshHz = 144 },
    };

    private TweakContext Ctx(SystemSnapshot s) => new()
    {
        Snapshot = s,
        Registry = _reg,
        FileSystem = _fs,
        Commands = new ProcessCommandRunner(),
    };

    // ---- locator ----

    [Fact]
    public void Locator_finds_game_settings_and_ragemp()
    {
        var gta = Install();

        Assert.True(gta.GameFound);
        Assert.Equal(GtaEdition.Steam, gta.Edition);
        Assert.Equal(GameDir, N(gta.GameDir));
        Assert.True(gta.SettingsExists);
        Assert.Equal(RageDir, N(gta.RageMpDir));
        Assert.False(gta.AnyRunning);
    }

    [Fact]
    public void Locator_accepts_a_registry_path_with_a_sub_folder_and_reports_running_processes()
    {
        _fs.WriteAllText(GameDir + @"\GTA5.exe", "x");
        _reg.SetValue(RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", "InstallFolder", RegValue.Str(GameDir + @"\GTAV\"));

        var gta = new GtaLocator(_fs, _reg, Docs).Locate(name => name == "ragemp_v");

        Assert.Equal(GameDir, N(gta.GameDir));
        Assert.Equal(GtaEdition.Rockstar, gta.Edition);
        Assert.True(gta.RageMpRunning);
        Assert.True(gta.AnyRunning);
        Assert.False(gta.SettingsExists);
    }

    [Fact]
    public void Locator_reports_nothing_when_gta_is_missing()
    {
        var gta = new GtaLocator(_fs, _reg, Docs).Locate();
        Assert.False(gta.GameFound);
        Assert.Null(gta.RageMpDir);
    }

    // ---- settings editor ----

    [Fact]
    public void Settings_editor_changes_only_the_value_it_was_asked_to()
    {
        var file = GtaSettingsFile.Parse(SettingsText);

        Assert.True(file.TrySet("ShadowQuality", "0"));
        Assert.True(file.TrySet("LodScale", "0.4"));

        Assert.Equal("0", file.Get("ShadowQuality"));
        Assert.Equal("0.400000", file.Get("LodScale"));
        // Everything else is untouched, including the MSAA / MSAAFragments pair that share a prefix.
        Assert.Equal("4", file.Get("MSAA"));
        Assert.Equal("0", file.Get("MSAAFragments"));
        Assert.Equal(SettingsText.Replace("<ShadowQuality value=\"3\"", "<ShadowQuality value=\"0\"").Replace("<LodScale value=\"1.000000\"", "<LodScale value=\"0.400000\""), file.Text);
    }

    [Fact]
    public void Settings_editor_never_invents_keys_and_treats_equal_numbers_as_equal()
    {
        var file = GtaSettingsFile.Parse(SettingsText);

        Assert.False(file.TrySet("NoSuchKey", "1"));
        Assert.False(file.Has("NoSuchKey"));
        Assert.False(file.TrySet("LodScale", "1"));
        Assert.True(file.Is("Shadow_Distance", "2"));
        Assert.False(file.TrySet("DoF", "TRUE"));
        Assert.Equal(SettingsText, file.Text);
    }

    [Fact]
    public void Settings_editor_refuses_a_broken_file()
    {
        Assert.Throws<InvalidDataException>(() => GtaSettingsFile.Parse("<Settings><graphics>"));
        Assert.Throws<InvalidDataException>(() => GtaSettingsFile.Parse("<Other/>"));
    }

    // ---- modes ----

    [Fact]
    public void Modes_only_use_keys_that_exist_in_a_real_settings_file()
    {
        var file = GtaSettingsFile.Parse(SettingsText);
        foreach (var mode in GtaModes.All)
        {
            Assert.All(mode.Values.Keys, key => Assert.True(file.Has(key), $"{mode.Id}: {key}"));
        }
    }

    [Fact]
    public void Five_modes_two_kinds()
    {
        Assert.Equal(5, GtaModes.All.Count);
        Assert.Equal(3, GtaModes.All.Count(m => m.Kind == GtaModeKind.Performance));
        Assert.Equal(2, GtaModes.All.Count(m => m.Kind == GtaModeKind.Roleplay));
        Assert.Equal(GtaModes.All.Count, GtaModes.All.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Potato_mode_is_lighter_than_weak_and_pretty_never_turns_on_wasteful_effects()
    {
        int V(string id, string key) => (int)double.Parse(GtaModes.Get(id).Values[key], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(V(GtaModes.Potato, "ShadowQuality") <= V(GtaModes.Weak, "ShadowQuality"));
        Assert.True(V(GtaModes.Weak, "TextureQuality") >= V(GtaModes.Potato, "TextureQuality"));
        foreach (var id in new[] { GtaModes.Pretty, GtaModes.Hybrid })
        {
            var m = GtaModes.Get(id);
            Assert.Equal("0", m.Values["MotionBlurStrength"]);
            Assert.Equal("false", m.Values["DoF"]);
            Assert.Equal("0", m.Values["MSAA"]);
            Assert.Equal("false", m.Values["UltraShadows_Enabled"]);
            Assert.Equal("false", m.Values["Shadow_LongShadows"]);
        }

        // The hybrid keeps the visible parts of the beautiful mode and the cheap parts of the medium mode.
        Assert.Equal(GtaModes.Get(GtaModes.Pretty).Values["TextureQuality"], GtaModes.Get(GtaModes.Hybrid).Values["TextureQuality"]);
        Assert.Equal(GtaModes.Get(GtaModes.Medium).Values["ShadowQuality"], GtaModes.Get(GtaModes.Hybrid).Values["ShadowQuality"]);
        Assert.Equal("0", GtaModes.Get(GtaModes.Hybrid).Values["Tessellation"]);
    }

    [Fact]
    public void Textures_follow_video_memory()
    {
        var pretty = GtaModes.Get(GtaModes.Pretty);
        string Tex(GpuInfo? g) => GtaModes.Resolve(pretty, g)["TextureQuality"];

        Assert.Equal("2", Tex(new GpuInfo { DedicatedVideoMemory = 8UL << 30 }));
        Assert.Equal("1", Tex(new GpuInfo { DedicatedVideoMemory = 3UL << 30 }));
        Assert.Equal("0", Tex(new GpuInfo { DedicatedVideoMemory = 1UL << 30 }));
        Assert.Equal("1", Tex(new GpuInfo { IsIntegrated = true, DedicatedVideoMemory = 512UL << 20 }));
        Assert.Equal("2", Tex(null));
        Assert.Equal("0", GtaModes.Resolve(GtaModes.Get(GtaModes.Potato), new GpuInfo { DedicatedVideoMemory = 8UL << 30 })["TextureQuality"]);
    }

    [Fact]
    public void Recommendation_follows_the_pc()
    {
        Assert.Equal(GtaModes.Potato, GtaModes.Recommend(Pc(PcProfile.Weak, 1, integrated: true)).Mode.Id);
        Assert.Equal(GtaModes.Potato, GtaModes.Recommend(Pc(PcProfile.Weak, 2)).Mode.Id);
        Assert.Equal(GtaModes.Potato, GtaModes.Recommend(Pc(PcProfile.Weak, 4, ramGb: 4)).Mode.Id);
        Assert.Equal(GtaModes.Weak, GtaModes.Recommend(Pc(PcProfile.Weak, 4)).Mode.Id);
        Assert.Equal(GtaModes.Hybrid, GtaModes.Recommend(Pc(PcProfile.Medium, 6)).Mode.Id);
        Assert.Equal(GtaModes.Pretty, GtaModes.Recommend(Pc(PcProfile.Strong, 12)).Mode.Id);
        Assert.Equal(GtaModes.Potato, GtaModes.Recommend(new SystemSnapshot { Profile = PcProfile.Weak }).Mode.Id);
    }

    [Fact]
    public void Applying_a_mode_writes_the_file_and_detection_recognises_it()
    {
        var gta = Install();
        var pc = Pc(PcProfile.Medium, 6);
        var tweak = new GtaGraphicsModeTweak(gta, GtaModes.Get(GtaModes.Hybrid));

        var plan = tweak.Plan(Ctx(pc));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.True(plan.DefaultSelected, "the recommended mode for a medium PC is preselected");
        var action = Assert.Single(plan.Actions);
        Assert.Equal(Settings, N(action.Path));

        _fs.WriteAllText(Settings, action.Content!);
        var written = GtaSettingsFile.Parse(_fs.ReadAllText(Settings));
        Assert.Equal(GtaModes.Hybrid, GtaModes.Detect(written, pc.PrimaryGpu)?.Id);
        Assert.Equal("1", written.Get("VSync")); // the hybrid does not touch V-Sync or the screen mode
        Assert.Equal("2", written.Get("Windowed"));
        Assert.Equal(TweakStatus.AlreadyOptimal, tweak.Plan(Ctx(pc)).Status);
    }

    [Fact]
    public void Non_recommended_modes_are_available_but_not_preselected()
    {
        var gta = Install();
        var plan = new GtaGraphicsModeTweak(gta, GtaModes.Get(GtaModes.Potato)).Plan(Ctx(Pc(PcProfile.Strong, 12)));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.False(plan.DefaultSelected);
    }

    [Fact]
    public void Potato_lowers_a_16_9_resolution_but_leaves_other_aspect_ratios_alone()
    {
        var gta = Install();
        var potato = new GtaGraphicsModeTweak(gta, GtaModes.Get(GtaModes.Potato));

        var wide = GtaSettingsFile.Parse(potato.Plan(Ctx(Pc(PcProfile.Weak, 1))).Actions[0].Content!);
        Assert.Equal("1280", wide.Get("ScreenWidth"));
        Assert.Equal("720", wide.Get("ScreenHeight"));
        Assert.Equal("2", wide.Get("Windowed"));

        var ultrawide = potato.Plan(Ctx(Pc(PcProfile.Weak, 1, w: 3440, h: 1440)));
        var file = GtaSettingsFile.Parse(ultrawide.Actions[0].Content!);
        Assert.Equal("1920", file.Get("ScreenWidth"));
        Assert.Contains("не 16:9", ultrawide.StatusText);
    }

    [Fact]
    public void A_running_game_or_missing_settings_blocks_the_mode_instead_of_writing()
    {
        var tweak = (GtaInfo g) => new GtaGraphicsModeTweak(g, GtaModes.Get(GtaModes.Weak));

        Assert.Equal(TweakStatus.Blocked, tweak(Install(running: true)).Plan(Ctx(Pc(PcProfile.Weak, 4))).Status);

        var fresh = new InMemoryFileSystem();
        var gta = new GtaInfo { GameDir = GameDir, SettingsPath = Settings, SettingsExists = false };
        var plan = new GtaGraphicsModeTweak(gta, GtaModes.Get(GtaModes.Weak)).Plan(new TweakContext
        {
            Snapshot = Pc(PcProfile.Weak, 4), Registry = _reg, FileSystem = fresh, Commands = new ProcessCommandRunner(),
        });
        Assert.Equal(TweakStatus.NotApplicable, plan.Status);
        Assert.Contains("settings.xml", plan.StatusText);
    }

    [Fact]
    public void A_damaged_settings_file_is_reported_not_rewritten()
    {
        var gta = Install();
        _fs.WriteAllText(Settings, "<Settings><graphics>");

        var plan = new GtaGraphicsModeTweak(gta, GtaModes.Get(GtaModes.Weak)).Plan(Ctx(Pc(PcProfile.Weak, 4)));

        Assert.Equal(TweakStatus.Blocked, plan.Status);
        Assert.Empty(plan.Actions);
    }

    // ---- RAGE MP cache and the rest ----

    [Fact]
    public void Cache_reset_renames_the_folder_and_can_be_undone_by_renaming_back()
    {
        var gta = Install();
        var plan = new RageMpCacheTweak(gta).Plan(Ctx(Pc(PcProfile.Medium, 6)));

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.False(plan.DefaultSelected);
        var run = Assert.Single(plan.Actions);
        Assert.Contains("ren \"C:\\RAGEMP\\client_resources\" \"client_resources.bak\"", N(run.Arguments));
        Assert.Contains("ren \"C:\\RAGEMP\\client_resources.bak\" \"client_resources\"", N(run.UndoArguments));
    }

    [Fact]
    public void Cache_reset_never_overwrites_an_earlier_backup_or_runs_with_the_game_open()
    {
        var gta = Install();
        _fs.WriteAllText(RageDir + @"\client_resources.bak\old.dat", "old");
        var plan = new RageMpCacheTweak(gta).Plan(Ctx(Pc(PcProfile.Medium, 6)));
        Assert.Equal(TweakStatus.ManualOnly, plan.Status);
        Assert.Empty(plan.Actions);

        Assert.Equal(TweakStatus.Blocked, new RageMpCacheTweak(gta with { RageMpRunning = true }).Plan(Ctx(Pc(PcProfile.Medium, 6))).Status);
        Assert.Equal(TweakStatus.NotApplicable, new RageMpCacheTweak(new GtaInfo()).Plan(Ctx(Pc(PcProfile.Medium, 6))).Status);
    }

    [Fact]
    public void Gpu_preference_covers_gta_and_the_ragemp_client_on_hybrid_laptops_only()
    {
        var gta = Install();
        var hybrid = Pc(PcProfile.Medium, 6) with
        {
            Gpus = new[]
            {
                new GpuInfo { Name = "NVIDIA GeForce RTX 3050", Vendor = GpuVendor.Nvidia, DedicatedVideoMemory = 4UL << 30 },
                new GpuInfo { Name = "Intel UHD", Vendor = GpuVendor.Intel, IsIntegrated = true },
            },
        };

        var plan = new GtaGpuPreferenceTweak(gta).Plan(Ctx(hybrid));
        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.Equal(2, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Contains("GpuPreference=2", a.Value!.Data));
        Assert.True(plan.DefaultSelected);

        Assert.Equal(TweakStatus.NotApplicable, new GtaGpuPreferenceTweak(gta).Plan(Ctx(Pc(PcProfile.Medium, 6))).Status);
    }

    [Fact]
    public void Catalog_has_unique_ids_and_no_other_games_in_its_texts()
    {
        var tweaks = GtaCatalog.Create(Install());

        Assert.Equal(tweaks.Count, tweaks.Select(t => t.Id).Distinct().Count());
        Assert.Equal(5, tweaks.Count(t => t.Id.StartsWith(GtaCatalog.ModePrefix, StringComparison.Ordinal)));
        Assert.All(tweaks, t => Assert.DoesNotContain("Dota", t.Title + t.Explanation));
        Assert.All(tweaks, t => Assert.DoesNotContain("CS2", t.Title + t.Explanation));

        var plans = GtaCatalog.PlanAll(Ctx(Pc(PcProfile.Medium, 6)), tweaks);
        Assert.Equal(tweaks.Count, plans.Count);
        // Only one graphics mode is ever preselected, so the default "apply" cannot write the file twice.
        Assert.Single(plans.Where(p => p.DefaultSelected && p.Tweak.Id.StartsWith(GtaCatalog.ModePrefix, StringComparison.Ordinal)));
    }
}
