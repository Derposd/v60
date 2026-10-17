using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class VideoConfigTests
{
    internal const string Sample =
        "\"config\"\r\n" +
        "{\r\n" +
        "\t\"VendorID\"\t\t\"4318\"\r\n" +
        "\t\"setting.fullscreen\"\t\t\"1\"\r\n" +
        "\t\"setting.mat_vsync\"\t\t\"1\"\r\n" +
        "\t\"setting.mat_viewportscale\"\t\t\"1.000000\"\r\n" +
        "\t\"setting.defaultres\"\t\t\"1920\"\r\n" +
        "\t\"setting.cl_globallight_shadow_mode\"\t\t\"3\"\r\n" +
        "\t\"setting.r_ssao\"\t\t\"1\"\r\n" +
        "\t\"setting.dota_cheap_water\"\t\t\"0\"\r\n" +
        "\t\"setting.mat_picmip\"\t\t\"0\"\r\n" +
        "}\r\n";

    [Fact]
    public void Reads_values()
    {
        var cfg = VideoConfig.Parse(Sample);
        Assert.Equal("4318", cfg.Get("VendorID"));
        Assert.Equal("3", cfg.Get("setting.cl_globallight_shadow_mode"));
        Assert.Null(cfg.Get("setting.nonexistent"));
    }

    [Fact]
    public void TrySet_changes_existing_key_and_preserves_everything_else()
    {
        var cfg = VideoConfig.Parse(Sample);
        Assert.True(cfg.TrySet("setting.r_ssao", "0"));
        var text = cfg.ToString();
        Assert.Equal(Sample.Replace("\"setting.r_ssao\"\t\t\"1\"", "\"setting.r_ssao\"\t\t\"0\""), text);
        Assert.Contains("\r\n", text);
    }

    [Fact]
    public void TrySet_never_adds_missing_keys()
    {
        var cfg = VideoConfig.Parse(Sample);
        Assert.False(cfg.TrySet("setting.r_grass_quality", "0"));
        Assert.Equal(Sample, cfg.ToString());
    }

    [Fact]
    public void Untouched_file_roundtrips_byte_for_byte()
    {
        Assert.Equal(Sample, VideoConfig.Parse(Sample).ToString());
        var lf = Sample.Replace("\r\n", "\n").TrimEnd('\n');
        Assert.Equal(lf, VideoConfig.Parse(lf).ToString());
    }

    [Fact]
    public void Preset_reports_changes_and_missing_keys()
    {
        var settings = VideoPresets.Build(VideoPreset.MaxFps, PcProfile.Weak, 2.0);
        var app = VideoPresets.Apply(Sample, settings);

        Assert.Contains(app.Changes, c => c.Key == "setting.cl_globallight_shadow_mode" && c.From == "3" && c.To == "0");
        Assert.Contains(app.Changes, c => c.Key == "setting.mat_vsync" && c.To == "0");
        Assert.Contains(app.Changes, c => c.Key == "setting.mat_viewportscale" && c.To == "0.700000");
        Assert.Contains(app.Changes, c => c.Key == "setting.mat_picmip" && c.To == "1");
        Assert.Contains("setting.r_grass_quality", app.MissingKeys);
        Assert.DoesNotContain("r_grass_quality", app.NewText);
        Assert.Contains("\"VendorID\"\t\t\"4318\"", app.NewText);
    }

    [Fact]
    public void Preset_treats_equal_numbers_as_unchanged()
    {
        var settings = new Dictionary<string, string> { ["setting.mat_viewportscale"] = "1.0" };
        var app = VideoPresets.Apply(Sample, settings);
        Assert.Empty(app.Changes);
    }

    [Fact]
    public void Quality_preset_on_strong_pc_enables_effects()
    {
        var s = VideoPresets.Build(VideoPreset.Quality, PcProfile.Strong, 12);
        Assert.Equal("3", s["setting.cl_globallight_shadow_mode"]);
        Assert.Equal("1", s["setting.r_ssao"]);
        Assert.Equal("1.000000", s["setting.mat_viewportscale"]);
    }
}
