using System.Text.Json.Nodes;
using Dota2Optimizer.Core.Upscale;

namespace Dota2Optimizer.Tests;

public class MagpieTests
{
    private static MagpieGameProfile Game(string exe = @"C:\Games\Hades\Hades.exe") =>
        new(exe, "SDL_app", "Hades", UpscaleAlgorithm.Nis, UpscalePreset.Performance, 1, 0x10DE, 0x2504, AutoScale: true);

    [Fact]
    public void Empty_config_gets_our_modes_a_default_profile_and_no_update_checks()
    {
        var json = JsonNode.Parse(MagpieSetup.BuildConfig(string.Empty, UpscaleAlgorithm.Fsr, UpscalePreset.Balance, new[] { Game() }))!;

        var modes = json["scalingModes"]!.AsArray();
        Assert.Equal(MagpieSetup.Algorithms.Length * 3, modes.Count);
        Assert.All(modes, m => Assert.StartsWith(MagpieSetup.ModePrefix, m!["name"]!.GetValue<string>()));
        Assert.Equal("FSR\\FSR_EASU", modes[MagpieSetup.IndexOf(UpscaleAlgorithm.Fsr, UpscalePreset.Balance)]!["effects"]![0]!["name"]!.GetValue<string>());
        Assert.False(json["autoCheckForUpdates"]!.GetValue<bool>());

        var profiles = json["profiles"]!.AsArray();
        Assert.Equal(MagpieSetup.IndexOf(UpscaleAlgorithm.Fsr, UpscalePreset.Balance), profiles[0]!["scalingMode"]!.GetValue<int>());
        var game = profiles[1]!;
        Assert.Equal(@"C:\Games\Hades\Hades.exe", game["pathRule"]!.GetValue<string>());
        Assert.Equal("SDL_app", game["classNameRule"]!.GetValue<string>());
        Assert.Equal(1, game["autoScale"]!.GetValue<int>());
        Assert.Equal(MagpieSetup.IndexOf(UpscaleAlgorithm.Nis, UpscalePreset.Performance), game["scalingMode"]!.GetValue<int>());
        Assert.Equal(1, game["graphicsCardId"]!["idx"]!.GetValue<int>());
    }

    [Fact]
    public void Users_own_modes_and_settings_survive_and_profiles_follow_them()
    {
        const string existing = """
        {
          "language": "ru",
          "scalingModes": [ { "name": "FrameLab · old", "effects": [] }, { "name": "My CRT", "effects": [ { "name": "CRT\\CRT_Geom" } ] } ],
          "profiles": [ { "scalingMode": 0 }, { "name": "Emu", "packaged": false, "pathRule": "C:\\emu.exe", "classNameRule": "X", "scalingMode": 1 } ]
        }
        """;

        var json = JsonNode.Parse(MagpieSetup.BuildConfig(existing, UpscaleAlgorithm.Lanczos, UpscalePreset.Quality, Array.Empty<MagpieGameProfile>()))!;

        Assert.Equal("ru", json["language"]!.GetValue<string>());
        var modes = json["scalingModes"]!.AsArray();
        var crt = MagpieSetup.Algorithms.Length * 3;
        Assert.Equal("My CRT", modes[crt]!["name"]!.GetValue<string>());
        Assert.Equal(crt + 1, modes.Count);
        Assert.Equal(crt, json["profiles"]![1]!["scalingMode"]!.GetValue<int>());
    }

    [Fact]
    public void Applying_twice_does_not_duplicate_anything()
    {
        var once = MagpieSetup.BuildConfig(string.Empty, UpscaleAlgorithm.Fsr, UpscalePreset.Quality, new[] { Game() });
        var twice = MagpieSetup.BuildConfig(once, UpscaleAlgorithm.Fsr, UpscalePreset.Quality, new[] { Game() });
        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData(UpscalePreset.Quality, UpscaleAlgorithm.Fsr, 1970, 1108)]
    [InlineData(UpscalePreset.Balance, UpscaleAlgorithm.Fsr, 1706, 960)]
    [InlineData(UpscalePreset.Performance, UpscaleAlgorithm.Fsr, 1280, 720)]
    [InlineData(UpscalePreset.Quality, UpscaleAlgorithm.Integer, 1280, 720)]
    public void Game_resolution_for_each_preset_on_1440p(UpscalePreset p, UpscaleAlgorithm a, int w, int h) =>
        Assert.Equal((w, h), MagpieSetup.GameResolution(2560, 1440, p, a));
}
