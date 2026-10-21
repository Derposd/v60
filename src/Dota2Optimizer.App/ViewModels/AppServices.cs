using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.App.ViewModels;

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
    }

    public IFileSystem Fs { get; } = new RealFileSystem();
    public IRegistry Registry { get; } = new WindowsRegistry();
    public ICommandRunner Commands { get; } = new ProcessCommandRunner();
    public BackupStore Store { get; }
    public ChangeExecutor Executor { get; }
    public HistoryStore History { get; }
    public SnapshotCache ScanCache { get; }

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

    public void NotifyChangesApplied() => ChangesApplied?.Invoke();

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

        if (files.Any(f => f.EndsWith("localconfig.vdf", StringComparison.OrdinalIgnoreCase)) && SystemScanner.IsProcessRunning("steam"))
        {
            return "Закройте Steam полностью: иначе он перезапишет восстановленный localconfig.vdf.";
        }

        return null;
    }

    public SystemSnapshot Scan() => new SystemScanner(Registry, Fs, Commands).Scan();

    /// <summary>The saved scan with games and Windows settings re-read, or null when a full scan is needed.</summary>
    public SystemSnapshot? QuickScan() =>
        ScanCache.Load() is { } cached ? new SystemScanner(Registry, Fs, Commands).Refresh(cached, DateTime.Now) : null;

    /// <summary>
    /// Runs a request in an elevated copy of this exe (one UAC prompt). Returns null if the user declined.
    /// </summary>
    public static ElevatedResult? RunElevated(ElevatedRequest request)
    {
        request.BackupDir = AppPaths.Backups;
        var path = ElevatedHost.WriteRequest(request);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к программе");
        var code = ElevatedProcess.RunAs(exe, $"{ElevatedHost.Argument} \"{path}\" --root \"{AppPaths.Root}\"", TimeSpan.FromMinutes(5));
        if (code == -1)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }

            return null;
        }

        return ElevatedHost.ReadResult(request);
    }
}
