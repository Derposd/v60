using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

public sealed record PreviewLine(string Target, string Before, string After, string Description, bool RequiresAdmin);

public sealed record ApplyResult(bool Success, bool DryRun, string? ChangeSetId, IReadOnlyList<PreviewLine> Lines, string? Error)
{
    public static ApplyResult Fail(string error, IReadOnlyList<PreviewLine> lines) => new(false, false, null, lines, error);
}

public sealed record RollbackResult(bool Success, IReadOnlyList<string> Messages);

/// <summary>
/// Executes planned actions with a write-ahead backup and rolls them back.
/// The same class runs in-process (user-level changes) and in the elevated helper (admin changes).
/// </summary>
public sealed class ChangeExecutor
{
    private const int PreviewMaxChars = 120;

    private readonly IFileSystem _fs;
    private readonly IRegistry _reg;
    private readonly ICommandRunner _cmd;
    private readonly BackupStore _store;

    public ChangeExecutor(IFileSystem fs, IRegistry reg, ICommandRunner cmd, BackupStore store)
    {
        _fs = fs;
        _reg = reg;
        _cmd = cmd;
        _store = store;
    }

    public BackupStore Store => _store;

    /// <summary>Describes what each action would change without touching anything.</summary>
    public IReadOnlyList<PreviewLine> Preview(IEnumerable<PlannedAction> actions) =>
        actions.Select(a => a.Kind switch
        {
            ActionKind.SetRegistry => new PreviewLine(a.Target, Show(_reg.GetValue(a.Hive, a.Key!, a.ValueName!)), Show(a.Value), a.Description, a.RequiresAdmin),
            ActionKind.DeleteRegistry => new PreviewLine(a.Target, Show(_reg.GetValue(a.Hive, a.Key!, a.ValueName!)), "(удалить)", a.Description, a.RequiresAdmin),
            ActionKind.WriteFile => new PreviewLine(a.Target, _fs.FileExists(a.Path!) ? "файл (будет сохранена копия)" : "(нет файла)", "изменённый файл", a.Description, a.RequiresAdmin),
            _ => new PreviewLine(a.Target, "—", "выполнить команду", a.Description, a.RequiresAdmin),
        }).ToList();

    public ApplyResult Apply(string tweakId, string title, IReadOnlyList<PlannedAction> actions, bool dryRun)
    {
        var preview = Preview(actions);
        if (dryRun)
        {
            Log.Info($"[dry-run] {title}: {actions.Count} изменений, ничего не применено");
            return new ApplyResult(true, true, null, preview, null);
        }

        var set = new ChangeSet { TweakId = tweakId, Title = title };
        try
        {
            foreach (var action in actions)
            {
                var record = Backup(set, action);
                set.Records.Add(record);
                _store.Save(set); // write-ahead: the backup is on disk before the change happens
                Execute(action);
                Log.Info($"Применено: {action.Description} ({action.Target})");
            }

            _store.Save(set);
            return new ApplyResult(true, false, set.Id, preview, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            Log.Error($"Ошибка при применении «{title}», откатываю уже сделанные шаги", ex);
            set.Failed = true;
            RollbackRecords(set);
            set.RolledBack = true;
            set.RolledBackUtc = DateTime.UtcNow;
            _store.Save(set);
            return ApplyResult.Fail(ex.Message, preview);
        }
    }

    public RollbackResult Rollback(string changeSetId)
    {
        var set = _store.Find(changeSetId);
        if (set is null)
        {
            return new RollbackResult(false, new[] { "Набор изменений не найден" });
        }

        if (set.RolledBack)
        {
            return new RollbackResult(true, new[] { "Уже откачено" });
        }

        var messages = RollbackRecords(set);
        set.RolledBack = true;
        set.RolledBackUtc = DateTime.UtcNow;
        _store.Save(set);
        Log.Info($"Откат «{set.Title}»: {string.Join("; ", messages)}");
        return new RollbackResult(messages.All(m => !m.StartsWith("Ошибка", StringComparison.Ordinal)), messages);
    }

    /// <summary>Rolls back every active change set, newest first, so overlapping changes unwind correctly.</summary>
    public RollbackResult RollbackAll(bool includeAdmin)
    {
        var messages = new List<string>();
        var ok = true;
        foreach (var set in _store.Active().OrderByDescending(s => s.CreatedUtc))
        {
            if (set.RequiresAdminToRollback && !includeAdmin)
            {
                messages.Add($"Пропущено (нужны права администратора): {set.Title}");
                ok = false;
                continue;
            }

            var r = Rollback(set.Id);
            ok &= r.Success;
            messages.Add($"{set.Title}: {string.Join("; ", r.Messages)}");
        }

        return new RollbackResult(ok, messages);
    }

    private BackupRecord Backup(ChangeSet set, PlannedAction action) => action.Kind switch
    {
        ActionKind.SetRegistry or ActionKind.DeleteRegistry => new BackupRecord
        {
            Kind = BackupKind.Registry,
            Hive = action.Hive,
            Key = action.Key,
            ValueName = action.ValueName,
            Previous = _reg.GetValue(action.Hive, action.Key!, action.ValueName!),
            Description = action.Description,
            RequiresAdmin = action.RequiresAdmin,
        },
        ActionKind.WriteFile => _store.BackupFile(set, action.Path!, action.Description, action.RequiresAdmin),
        _ => new BackupRecord
        {
            Kind = BackupKind.Command,
            UndoCommand = action.UndoCommand,
            UndoArguments = action.UndoArguments,
            Description = action.Description,
            RequiresAdmin = action.RequiresAdmin,
        },
    };

    private void Execute(PlannedAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.SetRegistry:
                _reg.SetValue(action.Hive, action.Key!, action.ValueName!, action.Value!);
                break;
            case ActionKind.DeleteRegistry:
                _reg.DeleteValue(action.Hive, action.Key!, action.ValueName!);
                break;
            case ActionKind.WriteFile:
                _fs.WriteAllText(action.Path!, action.Content!);
                break;
            case ActionKind.RunCommand:
                var result = _cmd.Run(action.Command!, action.Arguments ?? string.Empty);
                if (!result.Success)
                {
                    throw new InvalidOperationException($"Команда «{action.Command} {action.Arguments}» вернула код {result.ExitCode}: {result.StdErr}{result.StdOut}".Trim());
                }

                break;
        }
    }

    private List<string> RollbackRecords(ChangeSet set)
    {
        var messages = new List<string>();
        foreach (var r in Enumerable.Reverse(set.Records))
        {
            try
            {
                switch (r.Kind)
                {
                    case BackupKind.Registry when r.Previous is null:
                        _reg.DeleteValue(r.Hive, r.Key!, r.ValueName!);
                        messages.Add($"Удалено значение {r.Target} (его не было до изменения)");
                        break;
                    case BackupKind.Registry:
                        _reg.SetValue(r.Hive, r.Key!, r.ValueName!, r.Previous);
                        messages.Add($"Восстановлено {r.Target} = {Show(r.Previous)}");
                        break;
                    case BackupKind.File when r.Existed:
                        _fs.WriteAllBytes(r.Path!, _fs.ReadAllBytes(r.BackupFile!));
                        messages.Add($"Восстановлен файл {r.Path}");
                        break;
                    case BackupKind.File:
                        _fs.DeleteFile(r.Path!);
                        messages.Add($"Удалён созданный файл {r.Path}");
                        break;
                    case BackupKind.Command when !string.IsNullOrEmpty(r.UndoCommand):
                        var res = _cmd.Run(r.UndoCommand!, r.UndoArguments ?? string.Empty);
                        messages.Add(res.Success ? $"Выполнено: {r.UndoCommand} {r.UndoArguments}" : $"Ошибка: {r.UndoCommand} вернула {res.ExitCode}");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
            {
                messages.Add($"Ошибка отката {r.Target}: {ex.Message}");
                Log.Error($"Ошибка отката {r.Target}", ex);
            }
        }

        return messages;
    }

    private static string Show(RegValue? v)
    {
        if (v is null)
        {
            return "(нет значения)";
        }

        return v.Data.Length > PreviewMaxChars ? v.Data[..PreviewMaxChars] + "…" : v.Data;
    }
}
