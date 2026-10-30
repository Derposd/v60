using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>
/// One tweak as a switch. "On" means the setting is in the tweak's state; switching it off rolls back exactly
/// the backup this app made. If Windows was already set that way before, there is nothing of ours to roll back,
/// and the switch says so instead of guessing a "default" value.
/// </summary>
public sealed record TweakToggle(Tweak Tweak, TweakPlan Plan, TweakState State, IReadOnlyList<ChangeSet> OwnChanges)
{
    public bool IsOn => State == TweakState.Applied || OwnChanges.Count > 0;
    public bool CanTurnOn => !IsOn && Plan.Status == TweakStatus.CanApply;
    public bool CanTurnOff => OwnChanges.Count > 0;

    /// <summary>Switching is possible at all right now (in one direction or the other).</summary>
    public bool CanToggle => CanTurnOn || CanTurnOff;

    public string Note =>
        CanTurnOff ? "Включено программой — выключение вернёт прежние значения из копии."
        : IsOn ? "Так уже настроено в Windows — программа ничего не меняла, выключать нечего."
        : Plan.StatusText;
}

public static class TweakEngine
{
    public static TweakToggle Evaluate(Tweak tweak, TweakContext ctx, BackupStore store)
    {
        TweakPlan plan;
        try
        {
            plan = tweak.Plan(ctx);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error($"Не удалось проверить твик {tweak.Id}", ex);
            plan = new TweakPlan { Tweak = tweak, Status = TweakStatus.Blocked, StatusText = $"Ошибка проверки: {ex.Message}" };
        }

        return new TweakToggle(tweak, plan, Tweak.StateOf(plan), OwnChanges(tweak.Id, store));
    }

    public static IReadOnlyList<TweakToggle> EvaluateAll(IEnumerable<Tweak> tweaks, TweakContext ctx, BackupStore store) =>
        tweaks.Select(t => Evaluate(t, ctx, store)).ToList();

    /// <summary>Active change sets made by this tweak, newest first — the order they have to be rolled back in.</summary>
    public static IReadOnlyList<ChangeSet> OwnChanges(string tweakId, BackupStore store) =>
        store.Active().Where(c => c.TweakId == tweakId && !c.Failed).OrderByDescending(c => c.CreatedUtc).ToList();
}
