using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

public static class TweakCatalog
{
    private static readonly Dictionary<GameId, IReadOnlyList<Tweak>> Cache = Games.All.ToDictionary(g => g.Id, Create);

    /// <summary>Dota 2 tweaks (kept for existing callers).</summary>
    public static IReadOnlyList<Tweak> All => For(Games.Dota2);

    /// <summary>Tweaks for one game. Windows-wide tweaks (power plan, Game DVR…) share their id across games.</summary>
    public static IReadOnlyList<Tweak> For(GameDefinition game) => Cache[game.Id];

    private static IReadOnlyList<Tweak> Create(GameDefinition game) => new Tweak[]
    {
        new VideoPresetTweak(game),
        new LaunchOptionsTweak(game),
        new RefreshRateTweak(),
        new LowLatencyModeTweak(game),
        new GpuPreferenceTweak(game),
        new PowerPlanTweak(),
        new GameDvrTweak(),
        new GameModeTweak(),
        new AccessibilityHotkeysTweak(),
        new MouseAccelerationTweak(game),
        new UsbSelectiveSuspendTweak(),
        new ExclusiveFullscreenTweak(game),
        new WindowedGameOptimizationsTweak(game),
        new FullscreenOptimizationsTweak(game),
        new HagsTweak(),
        new MultiplaneOverlayTweak(),
        new GlobalTimerResolutionTweak(),
        new MemoryIntegrityTweak(),
        new PowerThrottlingTweak(),
        new MmcssGamesTweak(),
        new DeliveryOptimizationTweak(),
        new CpuPriorityTweak(game),
    };

    public static IReadOnlyList<TweakPlan> PlanAll(TweakContext ctx) => PlanAll(ctx, Games.Dota2);

    public static IReadOnlyList<TweakPlan> PlanAll(TweakContext ctx, GameDefinition game)
    {
        var plans = new List<TweakPlan>();
        foreach (var t in For(game))
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
    public static GameOptions DefaultGameOptions(SystemSnapshot s, string? currentLaunchOptions = null) =>
        DefaultGameOptions(Games.Dota2, s, currentLaunchOptions);

    public static GameOptions DefaultGameOptions(GameDefinition game, SystemSnapshot s, string? currentLaunchOptions = null)
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
        var catalog = game.Launch;
        if (game.Id == GameId.Dota2)
        {
            options.LaunchTokens.Add(LaunchOptionCatalog.NoVid.Token);
        }

        if (game.Id == GameId.Cs2)
        {
            // A competitive shooter: favour frame rate on every PC, quality is a choice.
            options.Preset = s.Profile == PcProfile.Strong ? VideoPreset.Balance : VideoPreset.MaxFps;
        }

        var weakOrLaptop = s.Profile == PcProfile.Weak || s.IsLaptop;
        if (weakOrLaptop)
        {
            options.LaunchTokens.Add(catalog.FpsMax.Token);
        }

        options.FpsMax = LaunchOptionsComposer.SuggestFpsMax(s.Display?.RefreshHz ?? 60, weakOrLaptop, variableRefresh: false);

        // Keep whatever managed options the user already has (API choice, -console…); we never drop or force them silently.
        foreach (var token in LaunchOptionsComposer.Analyze(currentLaunchOptions, catalog).ManagedTokens)
        {
            var parts = token.Split(' ');
            options.LaunchTokens.Add(catalog.Find(token)?.Token ?? parts[0]);
            if (parts[0].Equals(catalog.FpsMax.Token, StringComparison.OrdinalIgnoreCase) && parts.Length > 1 &&
                int.TryParse(parts[1], out var fps))
            {
                options.FpsMax = fps;
            }
        }

        return options;
    }
}
