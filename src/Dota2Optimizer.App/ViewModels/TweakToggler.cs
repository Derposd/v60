using Dota2Optimizer.App.Views;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>Shared switch logic: on = preview + apply, off = roll back this app's backup. Used by every page that shows tweak rows.</summary>
public static class TweakToggler
{
    /// <summary>Toggles one row; returns the status text, or null when the user cancelled. Re-evaluates the row afterwards.</summary>
    public static async Task<string?> ToggleAsync(AppServices s, TweakRowVm row)
    {
        if (row.State is not { } state)
        {
            return null;
        }

        row.IsBusy = true;
        MarkState? result = null;
        string? status = null;
        try
        {
            if (state.CanTurnOff)
            {
                var messages = await s.RevertTweakAsync(row.Tweak.Id, row.Title);
                if (messages is not null)
                {
                    status = $"«{row.Title}» выключено: " + string.Join("; ", messages);
                    result = messages.Any(m => m.StartsWith("Ошибка", StringComparison.Ordinal)) ? MarkState.Error : MarkState.Undone;
                }
            }
            else if (state.CanTurnOn)
            {
                var lines = await s.ConfirmAndApplyAsync(new[] { state.Plan }, row.Title);
                if (lines is not null)
                {
                    status = string.Join("\n", lines.Select(l => l.Text));
                    result = lines.Any(l => l.Text.StartsWith("✗", StringComparison.Ordinal) || l.Text.StartsWith("Пропущено", StringComparison.Ordinal))
                        ? MarkState.Error
                        : MarkState.Done;
                }
            }

            await EvaluateAsync(s, new[] { row });
        }
        finally
        {
            row.IsBusy = false;
            row.Mark = result ?? (row.Mark == MarkState.Busy ? MarkState.None : row.Mark);
        }

        return status;
    }

    /// <summary>Re-reads the state of rows off the UI thread.</summary>
    public static async Task EvaluateAsync(AppServices s, IReadOnlyList<TweakRowVm> rows)
    {
        var snapshot = rows.Any(r => r.Tweak.Id.Contains("powerplan", StringComparison.Ordinal))
            ? await Task.Run(s.Rescan)
            : await s.EnsureSnapshotAsync();
        if (!ReferenceEquals(snapshot, s.Snapshot))
        {
            s.Snapshot = snapshot;
        }

        var ctx = s.Context(snapshot);
        var states = await Task.Run(() => rows.Select(r => TweakEngine.Evaluate(r.Tweak, ctx, s.Store)).ToList());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Update(states[i]);
        }
    }
}
