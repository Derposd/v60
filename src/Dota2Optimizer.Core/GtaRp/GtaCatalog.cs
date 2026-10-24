using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.GtaRp;

/// <summary>Everything the app can do for GTA V roleplay projects, in the same shape as the Dota 2 / CS2 catalogs.</summary>
public static class GtaCatalog
{
    public const string ModePrefix = "gta.mode.";

    public static IReadOnlyList<Tweak> Create(GtaInfo gta)
    {
        var list = new List<Tweak>();
        list.AddRange(GtaModes.All.Select(m => new GtaGraphicsModeTweak(gta, m)));
        list.Add(new GtaGpuPreferenceTweak(gta));
        list.Add(new RageMpCacheTweak(gta));

        // Windows-wide tweaks that do not depend on the game.
        list.Add(new RefreshRateTweak());
        list.Add(new PowerPlanTweak());
        list.Add(new GameModeTweak());
        list.Add(new GameDvrTweak());
        list.Add(new AccessibilityHotkeysTweak());
        list.Add(new UsbSelectiveSuspendTweak());
        list.Add(new MultiplaneOverlayTweak());
        list.Add(new GlobalTimerResolutionTweak());
        list.Add(new MemoryIntegrityTweak());
        list.Add(new PowerThrottlingTweak());
        list.Add(new DeliveryOptimizationTweak());
        return list;
    }

    public static IReadOnlyList<TweakPlan> PlanAll(TweakContext ctx, IReadOnlyList<Tweak> tweaks)
    {
        var plans = new List<TweakPlan>();
        foreach (var t in tweaks)
        {
            try
            {
                plans.Add(t.Plan(ctx));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Error($"Не удалось подготовить твик {t.Id}", ex);
                plans.Add(new TweakPlan { Tweak = t, Status = TweakStatus.Blocked, StatusText = $"Ошибка проверки: {ex.Message}" });
            }
        }

        return plans;
    }
}
