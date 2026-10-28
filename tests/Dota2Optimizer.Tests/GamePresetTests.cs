using Dota2Optimizer.Core.GamePresets;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class GamePresetTests
{
    private sealed class NoCommands : ICommandRunner
    {
        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null) => new(0, string.Empty, string.Empty);
    }

    private static readonly ConfigRoots Roots = new(@"C:\U\AppData\Local", @"C:\U\AppData\Roaming", @"C:\U");
    private readonly InMemoryFileSystem _fs = new();

    private TweakContext Ctx() => new() { Snapshot = new SystemSnapshot(), Registry = new InMemoryRegistry(), FileSystem = _fs, Commands = new NoCommands() };

    private GamePresetTweak Tweak(string game, string preset) => GamePresetCatalog.Tweaks(Roots).Single(t => t.Game.Id == game && t.Preset.Id == preset);

    [Fact]
    public void Ini_changes_only_existing_keys_in_the_right_section_and_keeps_the_rest()
    {
        const string ini = "[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\nsg.TextureQuality=3\r\n; comment\r\n[Other]\r\nsg.ShadowQuality=3\r\n";
        var cfg = ConfigText.Parse(ConfigFormat.Ini, ini);

        Assert.True(cfg.TrySet("ScalabilityGroups", "sg.ShadowQuality", "0"));
        Assert.False(cfg.TrySet("ScalabilityGroups", "sg.Missing", "0"));
        Assert.Equal("[ScalabilityGroups]\r\nsg.ShadowQuality=0\r\nsg.TextureQuality=3\r\n; comment\r\n[Other]\r\nsg.ShadowQuality=3\r\n", cfg.ToString());
    }

    [Fact]
    public void Minecraft_values_keep_the_style_the_game_wrote()
    {
        var cfg = ConfigText.Parse(ConfigFormat.Colon, "version:3465\nrenderClouds:\"true\"\nao:2\nrenderDistance:16\n");

        cfg.TrySet(null, "renderClouds", "false");
        cfg.TrySet(null, "ao", "false");
        cfg.TrySet(null, "renderDistance", "8");

        Assert.Equal("version:3465\nrenderClouds:\"false\"\nao:0\nrenderDistance:8\n", cfg.ToString());
    }

    [Fact]
    public void Fortnite_preset_rewrites_the_config_and_reports_the_changes()
    {
        var path = Path.Combine(Roots.LocalAppData, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
        _fs.WriteAllText(path, "[/Script/FortniteGame.FortGameUserSettings]\r\nbMotionBlur=True\r\nFrameRateLimit=144.000000\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\nsg.ViewDistanceQuality=1\r\n");
        GamePresetTweak.IsRunning = _ => false;

        var plan = Tweak("fortnite", "perf").Plan(Ctx());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        var content = plan.Actions.Single().Content!;
        Assert.Contains("bMotionBlur=False", content);
        Assert.Contains("sg.ShadowQuality=0", content);
        Assert.Contains("FrameRateLimit=144.000000", content);
        Assert.Contains("Изменится значений: 2 (из 3", plan.StatusText);
    }

    [Fact]
    public void Preset_waits_for_the_game_to_close_and_needs_a_config()
    {
        Assert.Equal(TweakStatus.NotApplicable, Tweak("apex", "perf").Plan(Ctx()).Status);

        _fs.WriteAllText(Path.Combine(Roots.UserProfile, "Saved Games", "Respawn", "Apex", "local", "videoconfig.txt"),
            "\"VideoConfig\"\n{\n\t\"setting.csm_enabled\"\t\t\"1\"\n\t\"setting.mat_vsync_mode\"\t\t\"0\"\n}\n");
        GamePresetTweak.IsRunning = p => p == "r5apex";
        Assert.Equal(TweakStatus.Blocked, Tweak("apex", "perf").Plan(Ctx()).Status);

        GamePresetTweak.IsRunning = _ => false;
        var plan = Tweak("apex", "perf").Plan(Ctx());
        Assert.Contains("\"setting.csm_enabled\"\t\t\"0\"", plan.Actions.Single().Content);
    }

    [Fact]
    public void Valorant_configs_are_found_in_every_account_folder()
    {
        _fs.WriteAllText(Path.Combine(Roots.LocalAppData, "VALORANT", "Saved", "Config", "abc-eu", "Windows", "GameUserSettings.ini"), "[ScalabilityGroups]\nsg.ShadowQuality=2\n");
        _fs.WriteAllText(Path.Combine(Roots.LocalAppData, "VALORANT", "Saved", "Config", "def-eu", "Windows", "GameUserSettings.ini"), "[ScalabilityGroups]\nsg.ShadowQuality=0\n");
        GamePresetTweak.IsRunning = _ => false;

        var plan = Tweak("valorant", "perf").Plan(Ctx());

        Assert.Single(plan.Actions);
        Assert.Contains("abc-eu", plan.Actions[0].Path);
    }
}
