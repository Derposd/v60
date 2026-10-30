using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>
/// One line of an apply report; lines for applied changes carry the change set id for undo. Lines about one tweak carry its id,
/// whether it worked and the error, so a page can mark that row.
/// </summary>
public sealed record ApplyLine(string Text, string? ChangeSetId = null, string? TweakId = null, bool? Ok = null, string? Error = null);

/// <summary>Shared services and the latest system snapshot for all tabs.</summary>
public sealed class AppServices
{
    private SystemSnapshot? _snapshot;

    public AppServices()
    {
        Store = new BackupStore(Fs, AppPaths.Backups);
        Executor = new ChangeExecutor(Fs, Registry, Commands, Store);
        History = new HistoryStore(Fs, AppPaths.History);
        ScanCache = new SnapshotCache(Fs, AppPaths.ScanCache);
        Boost = new GameSessionBoost(Commands, Fs, AppPaths.SessionBoost);
    }

    public IFileSystem Fs { get; } = new RealFileSystem();
    public IRegistry Registry { get; } = new WindowsRegistry();
    public ICommandRunner Commands { get; } = new ProcessCommandRunner();
    public BackupStore Store { get; }
    public ChangeExecutor Executor { get; }
    public HistoryStore History { get; }
    public SnapshotCache ScanCache { get; }
    public GameSessionBoost Boost { get; }

    /// <summary>The saved scan is offered once per start; after that only the «Сканировать» button scans.</summary>
    public bool QuickScanTried { get; set; }

    public event Action? SnapshotChanged;
    public event Action? ChangesApplied;

    public SystemSnapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            if (value is not null)
            {
                ScanCache.Save(value);
            }

            SnapshotChanged?.Invoke();
        }
    }

    public void NotifyChangesApplied()
    {
        // Mouse and accessibility values are read by Windows at sign-in; push the new ones right away.
        UserInputSettings.Reload(Registry);
        ChangesApplied?.Invoke();
    }

    /// <summary>
    /// Applies plans with backups: system-level ones in one elevated copy (one UAC prompt, plus the restore point),
    /// per-user ones in this process. Returns the report lines.
    /// </summary>
    public async Task<IReadOnlyList<ApplyLine>> ApplyPlansAsync(IReadOnlyList<TweakPlan> selected, bool createRestorePoint)
    {
        var lines = new List<ApplyLine>();
        var admin = selected.Where(p => p.RequiresAdmin).ToList();
        var user = selected.Where(p => !p.RequiresAdmin).ToList();

        if (admin.Count > 0 || createRestorePoint)
        {
            var request = new ElevatedRequest
            {
                Mode = "apply",
                CreateRestorePoint = createRestorePoint,
                Items = admin.Select(p => new ElevatedItem { TweakId = p.Tweak.Id, Title = p.Tweak.Title, Actions = p.Actions.ToList() }).ToList(),
            };
            lines.Add(new ApplyLine(admin.Count > 0 ? "Запрашиваю права администратора (точка восстановления и системные настройки)…" : "Запрашиваю права администратора для точки восстановления…"));
            var result = await Task.Run(() => RunElevated(request));
            if (result is null)
            {
                lines.Add(new ApplyLine("Запрос прав администратора отклонён."));
                if (admin.Count > 0)
                {
                    lines.AddRange(admin.Select(a => new ApplyLine($"✗ {a.Tweak.Title}: пропущено, права администратора не выданы", TweakId: a.Tweak.Id, Ok: false, Error: "права администратора не выданы")));
                }

                if (user.Count > 0 && createRestorePoint &&
                    !Ui.Confirm("Точку восстановления создать не удалось (нужны права администратора).\n\nПродолжить без неё? Собственные копии файлов и реестра всё равно будут сохранены."))
                {
                    lines.Add(new ApplyLine("Отменено пользователем."));
                    return lines;
                }
            }
            else
            {
                if (result.RestorePointMessage is not null)
                {
                    lines.Add(new ApplyLine((result.RestorePointCreated == true ? "✓ " : "⚠ ") + "Точка восстановления: " + result.RestorePointMessage));
                }

                foreach (var r in result.Items)
                {
                    var title = admin.FirstOrDefault(a => a.Tweak.Id == r.TweakId)?.Tweak.Title ?? r.TweakId;
                    lines.Add(new ApplyLine(r.Success ? $"✓ {title}" : $"✗ {title}: {r.Error} (изменения этого пункта откачены)", r.Success ? r.ChangeSetId : null, r.TweakId, r.Success, r.Error));
                    History.Add(new HistoryEntry(DateTime.Now, r.Success ? "Применено" : "Ошибка", title, r.Error ?? string.Empty));
                }
            }
        }

        foreach (var p in user)
        {
            var r = await Task.Run(() => Executor.Apply(p.Tweak.Id, p.Tweak.Title, p.Actions, dryRun: false));
            lines.Add(new ApplyLine(r.Success ? $"✓ {p.Tweak.Title}" : $"✗ {p.Tweak.Title}: {r.Error} (изменения этого пункта откачены)", r.Success ? r.ChangeSetId : null, p.Tweak.Id, r.Success, r.Error));
            History.Add(new HistoryEntry(DateTime.Now, r.Success ? "Применено" : "Ошибка", p.Tweak.Title, string.Join("; ", p.Actions.Select(a => a.Description))));
        }

        return lines;
    }

    /// <summary>
    /// Shows the preview of every change (and the restore point offer before the first apply), then applies.
    /// Returns null when the user cancelled.
    /// </summary>
    public async Task<IReadOnlyList<ApplyLine>?> ConfirmAndApplyAsync(IReadOnlyList<TweakPlan> plans, string title)
    {
        if (plans.Count == 0)
        {
            return Array.Empty<ApplyLine>();
        }

        var preview = await Task.Run(() => plans.SelectMany(p => Executor.Preview(p.Actions)).ToList());
        var firstApply = !RestorePoint.CreatedBefore && Store.Load().Count == 0;
        var dialog = new Views.PreviewDialog(title, preview, firstApply, plans.Any(p => p.RequiresAdmin));
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var lines = await ApplyPlansAsync(plans, dialog.CreateRestorePoint);
        NotifyChangesApplied();
        return lines;
    }

    /// <summary>Switches a tweak off: rolls back every change this app made for it, newest first.</summary>
    public Task<IReadOnlyList<string>?> RevertTweakAsync(string tweakId, string title) =>
        UndoManyAsync(TweakEngine.OwnChanges(tweakId, Store).Select(c => c.Id).ToList(), title);

    /// <summary>Undoes several change sets, newest first, after one confirmation. Returns null when cancelled or blocked.</summary>
    public async Task<IReadOnlyList<string>?> UndoManyAsync(IReadOnlyList<string> changeSetIds, string title)
    {
        var sets = changeSetIds.Select(Store.Find).Where(s => s is { RolledBack: false }).Cast<ChangeSet>().OrderByDescending(s => s.CreatedUtc).ToList();
        if (sets.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (!Ui.Confirm($"Отменить «{title}»?\n\n{string.Join("\n", sets.Select(s => "• " + s.Title))}"))
        {
            return null;
        }

        if (sets.Select(GameOrSteamFileInvolved).FirstOrDefault(b => b is not null) is { } blocker)
        {
            Ui.Info(blocker);
            return null;
        }

        var messages = new List<string>();
        var adminIds = sets.Where(s => s.RequiresAdminToRollback && !Elevation.IsElevated).Select(s => s.Id).ToList();
        if (adminIds.Count > 0)
        {
            var result = await Task.Run(() => RunElevated(new ElevatedRequest { Mode = "rollback", ChangeSetIds = adminIds }));
            messages.AddRange(result?.Messages ?? new List<string> { "Права администратора не выданы — системные изменения не отменены." });
        }

        foreach (var set in sets.Where(s => !adminIds.Contains(s.Id)))
        {
            messages.AddRange((await Task.Run(() => Executor.Rollback(set.Id))).Messages);
        }

        History.Add(new HistoryEntry(DateTime.Now, "Откат", title, string.Join("; ", messages)));
        NotifyChangesApplied();
        return messages;
    }

    /// <summary>Latest still-active change set made by a given tweak, if any (used for the inline "Отменить" buttons).</summary>
    public ChangeSet? ActiveChangeFor(string tweakId) =>
        Store.Active().Where(c => c.TweakId == tweakId).OrderByDescending(c => c.CreatedUtc).FirstOrDefault();

    /// <summary>
    /// Undoes one change set after confirmation: warns about later overlapping changes, refuses while the
    /// game/Steam would overwrite the restored file, elevates only when the set needs admin rights.
    /// Returns null when the user cancelled or something blocked the undo.
    /// </summary>
    public async Task<IReadOnlyList<string>?> UndoAsync(string changeSetId)
    {
        var set = Store.Find(changeSetId);
        if (set is null || set.RolledBack)
        {
            return null;
        }

        var details = string.Join("\n", set.Records.Select(r => $"• {r.Description} — {r.Target}"));
        var conflicts = Store.LaterConflicts(changeSetId);
        var warn = conflicts.Count > 0
            ? $"\n\nВнимание: позже эти же настройки меняли: {string.Join(", ", conflicts.Select(c => c.Title))}. Лучше сначала отменить их (или «Откатить всё» на вкладке «Откат»)."
            : string.Empty;
        if (!Ui.Confirm($"Отменить «{set.Title}»?\n\n{details}{warn}"))
        {
            return null;
        }

        if (GameOrSteamFileInvolved(set) is { } blocker)
        {
            Ui.Info(blocker);
            return null;
        }

        IReadOnlyList<string> messages;
        if (set.RequiresAdminToRollback && !Elevation.IsElevated)
        {
            var result = await Task.Run(() => RunElevated(new ElevatedRequest { Mode = "rollback", ChangeSetIds = { set.Id } }));
            if (result is null)
            {
                Ui.Info("Запрос прав администратора отклонён — ничего не изменено.");
                return null;
            }

            messages = result.Messages;
        }
        else
        {
            messages = (await Task.Run(() => Executor.Rollback(set.Id))).Messages;
        }

        History.Add(new HistoryEntry(DateTime.Now, "Откат", set.Title, string.Join("; ", messages)));
        if (set.Records.Any(r => r.Target.Contains("HwSchMode", StringComparison.OrdinalIgnoreCase)))
        {
            messages = messages.Append("Перезагрузите компьютер, чтобы отмена HAGS вступила в силу.").ToList();
        }

        NotifyChangesApplied();
        return messages;
    }

    /// <summary>Restoring a video config or localconfig.vdf while the game/Steam runs would be overwritten by them on exit.</summary>
    public static string? GameOrSteamFileInvolved(ChangeSet set)
    {
        var files = set.Records.Where(r => r.Kind == BackupKind.File && !set.RolledBack).Select(r => r.Path ?? string.Empty).ToList();
        foreach (var game in Core.Game.Games.All)
        {
            if (files.Any(f => f.EndsWith(game.VideoFileName, StringComparison.OrdinalIgnoreCase) || f.EndsWith("autoexec.cfg", StringComparison.OrdinalIgnoreCase)) &&
                SystemScanner.IsProcessRunning(game.ProcessName))
            {
                return $"Закройте {game.Name}: иначе при выходе игра перезапишет восстановленный файл настроек.";
            }
        }

        // GTA V rewrites settings.xml on exit, and RAGE MP holds its resource cache open while it runs.
        var gtaFile = files.Any(f => f.Replace('/', '\\').EndsWith(@"GTA V\settings.xml", StringComparison.OrdinalIgnoreCase));
        if ((gtaFile || set.TweakId == "gta.ragemp.cache") &&
            (SystemScanner.IsProcessRunning(Path.GetFileNameWithoutExtension(Core.GtaRp.GtaInfo.GameExeName)) ||
             SystemScanner.IsProcessRunning(Path.GetFileNameWithoutExtension(Core.GtaRp.GtaInfo.RageMpExeName))))
        {
            return "Закройте GTA V и клиент RAGE MP: иначе при выходе игра перезапишет восстановленные настройки.";
        }

        if (files.Any(f => f.EndsWith("localconfig.vdf", StringComparison.OrdinalIgnoreCase)) && SystemScanner.IsProcessRunning("steam"))
        {
            return "Закройте Steam полностью: иначе он перезапишет восстановленный localconfig.vdf.";
        }

        return null;
    }

    /// <summary>The current snapshot, or the saved one re-read, or a full scan — whichever is available first.</summary>
    public async Task<SystemSnapshot> EnsureSnapshotAsync()
    {
        if (Snapshot is { } s)
        {
            return s;
        }

        var snap = await Task.Run(() => QuickScan() ?? Scan());
        Snapshot = snap;
        return snap;
    }

    public TweakContext Context(SystemSnapshot snapshot) => new() { Snapshot = snapshot, Registry = Registry, FileSystem = Fs, Commands = Commands };

    public SystemSnapshot Scan() => new SystemScanner(Registry, Fs, Commands).Scan();

    /// <summary>The saved scan with games and Windows settings re-read, or null when a full scan is needed.</summary>
    public SystemSnapshot? QuickScan() =>
        ScanCache.Load() is { } cached ? new SystemScanner(Registry, Fs, Commands).Refresh(cached, DateTime.Now) : null;

    /// <summary>Re-reads the PC after changes: the quick path when the saved scan still matches, a full scan otherwise.</summary>
    public SystemSnapshot Rescan() => QuickScan() ?? Scan();

    /// <summary>The elevated helper of this session: one UAC prompt, then reused until the window closes.</summary>
    public static ElevatedSession Elevated { get; } = new();

    /// <summary>Runs a request with administrator rights (asks once per session). Returns null if the user declined.</summary>
    public static ElevatedResult? RunElevated(ElevatedRequest request) => Elevated.Send(request);
}
