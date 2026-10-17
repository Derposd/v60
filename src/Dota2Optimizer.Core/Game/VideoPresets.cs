using System.Globalization;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Game;

public sealed record PresetChange(string Key, string From, string To);

public sealed record PresetApplication(string NewText, IReadOnlyList<PresetChange> Changes, IReadOnlyList<string> MissingKeys);

/// <summary>
/// Video presets expressed as video.txt keys. Keys come from real Dota 2 video.txt files;
/// a key that is absent in the user's file is skipped and reported, never added.
/// </summary>
public static class VideoPresets
{
    public static IReadOnlyDictionary<string, string> Build(VideoPreset preset, PcProfile profile, double vramGb)
    {
        var lowVram = vramGb > 0 && vramGb < 2.5;
        var s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (preset)
        {
            case VideoPreset.MaxFps:
                s["setting.cl_globallight_shadow_mode"] = "0";
                s["setting.r_ssao"] = "0";
                s["setting.r_deferred_height_fog"] = "0";
                s["setting.r_deferred_simple_light"] = "1";
                s["setting.r_deferred_specular"] = "0";
                s["setting.r_deferred_specular_bloom"] = "0";
                s["setting.r_deferred_additive_pass"] = "0";
                s["setting.r_screenspace_aa"] = "0";
                s["setting.dota_cheap_water"] = "1";
                s["setting.dota_ambient_creatures"] = "0";
                s["setting.dota_portrait_animate"] = "0";
                s["setting.r_dota_allow_wind_on_trees"] = "0";
                s["setting.r_dota_allow_parallax_mapping"] = "0";
                s["setting.r_grass_quality"] = "0";
                s["setting.mat_picmip"] = lowVram || profile == PcProfile.Weak ? "1" : "0";
                s["setting.mat_viewportscale"] = Scale(profile switch { PcProfile.Weak => 0.7, PcProfile.Medium => 0.85, _ => 1.0 });
                s["setting.mat_vsync"] = "0";
                s["setting.mat_triplebuffered"] = "0";
                break;

            case VideoPreset.Balance:
                s["setting.cl_globallight_shadow_mode"] = profile == PcProfile.Weak ? "0" : "1";
                s["setting.r_ssao"] = "0";
                s["setting.r_deferred_height_fog"] = "1";
                s["setting.r_deferred_simple_light"] = profile == PcProfile.Weak ? "1" : "0";
                s["setting.r_deferred_specular"] = "1";
                s["setting.r_deferred_specular_bloom"] = profile == PcProfile.Weak ? "0" : "1";
                s["setting.r_deferred_additive_pass"] = "1";
                s["setting.r_screenspace_aa"] = "1";
                s["setting.dota_cheap_water"] = "1";
                s["setting.dota_ambient_creatures"] = "1";
                s["setting.dota_portrait_animate"] = profile == PcProfile.Weak ? "0" : "1";
                s["setting.r_dota_allow_wind_on_trees"] = "1";
                s["setting.r_dota_allow_parallax_mapping"] = profile == PcProfile.Strong ? "1" : "0";
                s["setting.mat_picmip"] = lowVram ? "1" : "0";
                s["setting.mat_viewportscale"] = Scale(profile == PcProfile.Weak ? 0.85 : 1.0);
                s["setting.mat_vsync"] = "0";
                break;

            case VideoPreset.Quality:
                s["setting.cl_globallight_shadow_mode"] = profile == PcProfile.Strong ? "3" : "2";
                s["setting.r_ssao"] = "1";
                s["setting.r_deferred_height_fog"] = "1";
                s["setting.r_deferred_simple_light"] = "0";
                s["setting.r_deferred_specular"] = "1";
                s["setting.r_deferred_specular_bloom"] = "1";
                s["setting.r_deferred_additive_pass"] = "1";
                s["setting.r_screenspace_aa"] = "1";
                s["setting.dota_cheap_water"] = "0";
                s["setting.dota_ambient_creatures"] = "1";
                s["setting.dota_portrait_animate"] = "1";
                s["setting.r_dota_allow_wind_on_trees"] = "1";
                s["setting.r_dota_allow_parallax_mapping"] = "1";
                s["setting.r_grass_quality"] = "1";
                s["setting.mat_picmip"] = lowVram ? "1" : "0";
                s["setting.mat_viewportscale"] = Scale(1.0);
                break;
        }

        return s;
    }

    public static string Describe(VideoPreset p) => p switch
    {
        VideoPreset.MaxFps => "Максимум FPS",
        VideoPreset.Balance => "Баланс",
        _ => "Качество",
    };

    public static PresetApplication Apply(string videoTxt, IReadOnlyDictionary<string, string> settings)
    {
        var cfg = VideoConfig.Parse(videoTxt);
        var changes = new List<PresetChange>();
        var missing = new List<string>();
        foreach (var (key, value) in settings)
        {
            var current = cfg.Get(key);
            if (current is null)
            {
                missing.Add(key);
                continue;
            }

            if (!SameValue(current, value) && cfg.TrySet(key, value))
            {
                changes.Add(new PresetChange(key, current, value));
            }
        }

        return new PresetApplication(cfg.ToString(), changes, missing);
    }

    private static bool SameValue(string a, string b) =>
        double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
        double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? Math.Abs(x - y) < 1e-6
            : string.Equals(a, b, StringComparison.Ordinal);

    private static string Scale(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
