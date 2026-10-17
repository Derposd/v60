using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

public static class TweakCatalog
{
    public static IReadOnlyList<Tweak> All { get; } = new Tweak[]
    {
        new VideoPresetTweak(),
        new LaunchOptionsTweak(),
        new GpuPreferenceTweak(),
        new PowerPlanTweak(),
        new GameDvrTweak(),
        new GameModeTweak(),
        new ExclusiveFullscreenTweak(),
        new WindowedGameOptimizationsTweak(),
        new FullscreenOptimizationsTweak(),
        new HagsTweak(),
        new CpuPriorityTweak(),
    };

    public static IReadOnlyList<TweakPlan> PlanAll(TweakContext ctx)
    {
        var plans = new List<TweakPlan>();
        foreach (var t in All)
        {
            try
            {
                plans.Add(t.Plan(ctx));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Vdf.VdfParseException)
            {
                Log.Error($"Не удалось подготовить твик {t.Id}", ex);
                plans.Add(new TweakPlan { Tweak = t, Status = TweakStatus.Blocked, StatusText = $"Ошибка проверки: {ex.Message}" });
            }
        }

        return plans;
    }

    /// <summary>Default game options for this PC: preset by profile, sensible launch options, fps cap by refresh rate.</summary>
    public static GameOptions DefaultGameOptions(SystemSnapshot s, string? currentLaunchOptions = null)
    {
        var options = new GameOptions
        {
            Preset = s.Profile switch
            {
                PcProfile.Weak => VideoPreset.MaxFps,
                PcProfile.Medium => VideoPreset.Balance,
                _ => VideoPreset.Quality,
            },
        };
        options.LaunchTokens.Add(LaunchOptionCatalog.NoVid.Token);
        var weakOrLaptop = s.Profile == PcProfile.Weak || s.IsLaptop;
        if (weakOrLaptop)
        {
            options.LaunchTokens.Add(LaunchOptionCatalog.FpsMax.Token);
        }

        options.FpsMax = LaunchOptionsComposer.SuggestFpsMax(s.Display?.RefreshHz ?? 60, weakOrLaptop, variableRefresh: false);

        // Keep whatever managed options the user already has (API choice, -console…); we never drop or force them silently.
        foreach (var token in LaunchOptionsComposer.Analyze(currentLaunchOptions).ManagedTokens)
        {
            var parts = token.Split(' ');
            options.LaunchTokens.Add(LaunchOptionCatalog.All.FirstOrDefault(o => o.Token.Equals(token, StringComparison.OrdinalIgnoreCase))?.Token ?? parts[0]);
            if (parts[0].Equals(LaunchOptionCatalog.FpsMax.Token, StringComparison.OrdinalIgnoreCase) && parts.Length > 1 &&
                int.TryParse(parts[1], out var fps))
            {
                options.FpsMax = fps;
            }
        }

        return options;
    }
}
