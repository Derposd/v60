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
    }

    public IFileSystem Fs { get; } = new RealFileSystem();
    public IRegistry Registry { get; } = new WindowsRegistry();
    public ICommandRunner Commands { get; } = new ProcessCommandRunner();
    public BackupStore Store { get; }
    public ChangeExecutor Executor { get; }
    public HistoryStore History { get; }

    public event Action? SnapshotChanged;
    public event Action? ChangesApplied;

    public SystemSnapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            SnapshotChanged?.Invoke();
        }
    }

    public void NotifyChangesApplied() => ChangesApplied?.Invoke();

    public SystemSnapshot Scan() => new SystemScanner(Registry, Fs, Commands).Scan();

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
