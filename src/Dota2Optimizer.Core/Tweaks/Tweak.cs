using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

public enum Effectiveness
{
    High,
    Medium,
    Low,
    HardwareDependent,
}

public enum TweakCategory
{
    Game,
    Windows,
    Gpu,
}

public enum TweakStatus
{
    /// <summary>Doesn't apply to this PC (e.g. GPU preference on a single-GPU desktop).</summary>
    NotApplicable,

    /// <summary>Already in the recommended state.</summary>
    AlreadyOptimal,

    /// <summary>Can be applied automatically.</summary>
    CanApply,

    /// <summary>Has to be done by hand; instructions are provided.</summary>
    ManualOnly,

    /// <summary>Could be applied, but something blocks it right now (game or Steam running…).</summary>
    Blocked,
}

public static class EffectivenessText
{
    public static string Ru(this Effectiveness e) => e switch
    {
        Effectiveness.High => "Высокая",
        Effectiveness.Medium => "Средняя",
        Effectiveness.Low => "Низкая",
        _ => "Зависит от железа",
    };
}

public sealed class TweakPlan
{
    public required Tweak Tweak { get; init; }
    public TweakStatus Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public IReadOnlyList<PlannedAction> Actions { get; init; } = Array.Empty<PlannedAction>();

    /// <summary>Recommended for this specific PC (profile/hardware aware), not just in general.</summary>
    public bool RecommendedHere { get; init; }

    /// <summary>Text the user can copy when we refuse to write (e.g. launch options while Steam runs).</summary>
    public string? CopyText { get; init; }

    public bool DefaultSelected => Status == TweakStatus.CanApply && RecommendedHere && !Tweak.Disputed;
    public bool RequiresAdmin => Actions.Any(a => a.RequiresAdmin);
}

public enum VideoPreset
{
    MaxFps,
    Balance,
    Quality,
}

public sealed class GameOptions
{
    public VideoPreset Preset { get; set; } = VideoPreset.Balance;
    public HashSet<string> LaunchTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int? FpsMax { get; set; }
    public bool RemoveMythLaunchOptions { get; set; } = true;
    public bool VariableRefresh { get; set; }
}

public sealed class TweakContext
{
    public required SystemSnapshot Snapshot { get; init; }
    public required IRegistry Registry { get; init; }
    public required IFileSystem FileSystem { get; init; }
    public required ICommandRunner Commands { get; init; }
    public GameOptions Game { get; init; } = new();
}

/// <summary>
/// A single optimisation. Every tweak must explain itself honestly: what it does, how much it helps,
/// and whether it is recommended or merely optional.
/// </summary>
public abstract class Tweak
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract TweakCategory Category { get; }
    public abstract Effectiveness Effectiveness { get; }

    /// <summary>What it does and why, in one or two sentences.</summary>
    public abstract string Explanation { get; }

    /// <summary>General recommendation ("рекомендуется") vs optional ("по желанию").</summary>
    public virtual bool Recommended => true;

    /// <summary>Evidence is mixed: shown with a "спорно" tag and never selected by default.</summary>
    public virtual bool Disputed => false;

    public virtual bool RequiresRestart => false;
    public virtual string Risk => "Минимальный, полностью откатывается";

    /// <summary>Optional manual instructions shown next to the tweak.</summary>
    public virtual string? ManualSteps => null;

    public abstract TweakPlan Plan(TweakContext ctx);

    protected TweakPlan Result(TweakStatus status, string text, IReadOnlyList<PlannedAction>? actions = null, bool? recommendedHere = null, string? copy = null) => new()
    {
        Tweak = this,
        Status = status,
        StatusText = text,
        Actions = actions ?? Array.Empty<PlannedAction>(),
        RecommendedHere = recommendedHere ?? Recommended,
        CopyText = copy,
    };
}
