using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.GtaRp;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>Risk of a row on the «Спецификация изменений» screen; shown as text and shape, not only colour.</summary>
public enum SpecRisk
{
    Safe,
    Mid,
    Hard,
}

public enum SpecGame
{
    /// <summary>Windows and any game, not tied to one exe.</summary>
    General,
    Dota2,
    Cs2,
    GtaRp,
}

/// <summary>One row: a catalog tweak (with its backup, detection and rollback) and the group it is listed under.</summary>
public sealed record SpecItem(Tweak Tweak, string Group)
{
    public string Id => Tweak.Id;
    public string Name => Tweak.Title;
    public string Description => Tweak.Explanation;
    public SpecRisk Risk => SpecCatalog.RiskOf(Tweak.RiskLevel);
}

/// <summary>
/// A profile: its rows in display order. <see cref="Defaults"/> = null means «what the tweak recommends for this PC»
/// (<see cref="TweakPlan.DefaultSelected"/>); a profile can name its own set instead.
/// </summary>
public sealed record SpecProfile(SpecGame Game, string Title, IReadOnlyList<SpecItem> Items, IReadOnlySet<string>? Defaults = null);

/// <summary>
/// The main screen lists everything version 1.5 can change for a profile: «Общие настройки» — all Windows tweaks of the «ПК» section,
/// a game — its own catalog (graphics preset, launch options, the game's exe settings and the Windows tweaks that matter for it).
/// Rows reuse the catalog tweaks, so every page detects, applies and rolls back the same way and shares one backup per setting.
/// </summary>
public static class SpecCatalog
{
    /// <summary>«Общие настройки» first: the profile the app opens on.</summary>
    public static IReadOnlyList<SpecGame> Games { get; } = new[] { SpecGame.General, SpecGame.Dota2, SpecGame.Cs2, SpecGame.GtaRp };

    /// <summary>Rows that change the same setting in different ways: marking one unmarks the others.</summary>
    public static IReadOnlyList<IReadOnlySet<string>> Exclusive { get; } = new IReadOnlySet<string>[]
    {
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "win.powerplan", "win.powerplan.ultimate" },
    };

    private static readonly TweakCategory[] CategoryOrder =
    {
        TweakCategory.Game, TweakCategory.Windows, TweakCategory.Gpu, TweakCategory.Interface, TweakCategory.Privacy, TweakCategory.Services, TweakCategory.Network,
    };

    public static string TitleOf(SpecGame game) => game switch
    {
        SpecGame.General => "Общие настройки",
        SpecGame.Dota2 => "Dota 2",
        SpecGame.Cs2 => "Counter-Strike 2",
        _ => "GTA V · RP",
    };

    public static SpecRisk RiskOf(TweakRisk risk) => risk switch
    {
        TweakRisk.Low => SpecRisk.Safe,
        TweakRisk.Medium => SpecRisk.Mid,
        _ => SpecRisk.Hard,
    };

    /// <param name="gta">GTA V install when it was found; without it the GTA profile lists only its Windows tweaks.</param>
    public static SpecProfile For(SpecGame game, GtaInfo? gta = null)
    {
        IEnumerable<Tweak> tweaks = game switch
        {
            SpecGame.General => SystemTweakCatalog.All,
            SpecGame.Dota2 => TweakCatalog.For(Game.Games.Dota2),
            SpecGame.Cs2 => TweakCatalog.For(Game.Games.Cs2),
            // Graphics modes are one choice out of five (page «Режимы»), not switches: they stay on their page.
            _ => GtaCatalog.Create(gta ?? new GtaInfo()).Where(t => !t.Id.StartsWith(GtaCatalog.ModePrefix, StringComparison.Ordinal)),
        };

        // The game's own rows first, then the Windows groups in the order of the «Твики Windows» page; catalog order inside a group.
        var items = tweaks.Select(t => new SpecItem(t, GroupOf(game, t)))
            .OrderBy(i => i.Group == TitleOf(game) ? -1 : Array.IndexOf(CategoryOrder, i.Tweak.Category))
            .ToList();
        return new SpecProfile(game, TitleOf(game), items);
    }

    private static string GroupOf(SpecGame game, Tweak t)
    {
        if (game != SpecGame.General && t.Category == TweakCategory.Game)
        {
            return TitleOf(game);
        }

        if (game == SpecGame.GtaRp && t.Id.StartsWith("gta.", StringComparison.Ordinal))
        {
            return TitleOf(game);
        }

        return t.Category.Ru();
    }
}
