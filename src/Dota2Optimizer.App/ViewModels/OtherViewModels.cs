using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.ViewModels;

public sealed class BenchmarkViewModel : ObservableObject
{
    private readonly AppServices _s;
    private string _seconds = "60";
    private string _status;
    private string? _comparison;
    private bool _busy;

    public BenchmarkViewModel(AppServices services)
    {
        _s = services;
        PresentMonPath = BenchmarkService.FindPresentMon();
        _status = PresentMonPath is null
            ? "PresentMon не найден рядом с программой (папка tools). Используйте ручную инструкцию ниже."
            : "Запустите Dota 2, зайдите в одну и ту же сцену (демо героя или повтор матча) и нажмите «Замер». Понадобятся права администратора — так работает трассировка событий Windows (ETW).";
        CaptureBeforeCommand = new AsyncCommand(() => CaptureAsync("до"), () => !_busy && PresentMonPath is not null);
        CaptureAfterCommand = new AsyncCommand(() => CaptureAsync("после"), () => !_busy && PresentMonPath is not null);
        Reload();
    }

    public string? PresentMonPath { get; }
    public ICommand CaptureBeforeCommand { get; }
    public ICommand CaptureAfterCommand { get; }
    public ObservableCollection<BenchmarkRecord> Records { get; } = new();
    public string ManualInstructions => BenchmarkService.ManualInstructions;

    public string Seconds
    {
        get => _seconds;
        set => Set(ref _seconds, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string? Comparison
    {
        get => _comparison;
        private set => Set(ref _comparison, value);
    }

    private async Task CaptureAsync(string label)
    {
        if (!BenchmarkService.DotaRunning())
        {
            Ui.Info("Сначала запустите Dota 2 и зайдите в сцену для замера.");
            return;
        }

        var secs = int.TryParse(Seconds, out var v) ? Math.Clamp(v, 10, 600) : 60;
        _busy = true;
        Status = $"Идёт замер {secs} с… Не сворачивайте игру и оставайтесь в той же сцене.";
        try
        {
            var stats = await BenchmarkService.CaptureAsync(PresentMonPath!, secs, CancellationToken.None);
            if (stats is null)
            {
                Status = "Замер не удался: права администратора не выданы или PresentMon не увидел кадров dota2.exe. Попробуйте ещё раз или используйте ручную инструкцию.";
                return;
            }

            var record = new BenchmarkRecord(DateTime.Now, label, stats, null);
            _s.History.Add(record);
            Status = $"Замер «{label}»: средний {stats.AvgFps:0} FPS, 1% low {stats.OnePercentLowFps:0} FPS, просадок {stats.Stutters}.";
            Reload();
        }
        finally
        {
            _busy = false;
        }
    }

    public void Reload()
    {
        Records.Clear();
        foreach (var r in _s.History.Benchmarks.OrderByDescending(r => r.Time))
        {
            Records.Add(r);
        }

        var before = Records.FirstOrDefault(r => r.Label == "до");
        var after = Records.FirstOrDefault(r => r.Label == "после" && before is not null && r.Time > before.Time);
        Comparison = before is not null && after is not null ? HistoryStore.Compare(before, after) : null;
    }
}

public sealed class ChangeSetVm
{
    public ChangeSetVm(ChangeSet set)
    {
        Set = set;
    }

    public ChangeSet Set { get; }
    public string Title => Set.Title;
    public string When => Set.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string State => Set.Failed ? "ошибка, откачено автоматически" : Set.RolledBack ? $"откачено {Set.RolledBackUtc?.ToLocalTime():dd.MM HH:mm}" : "активно";
    public bool CanRollback => !Set.RolledBack;
    public string Details => string.Join("\n", Set.Records.Select(r => $"• {r.Description} — {r.Target}"));
    public string AdminText => Set.RequiresAdminToRollback ? "нужны права администратора" : string.Empty;
}

public sealed class RollbackViewModel : ObservableObject
{
    private readonly AppServices _s;
    private string _status = string.Empty;

    public RollbackViewModel(AppServices services)
    {
        _s = services;
        RollbackCommand = new AsyncCommand<ChangeSetVm>(RollbackOneAsync);
        RollbackAllCommand = new AsyncCommand(RollbackAllAsync, () => Sets.Any(s => s.CanRollback));
        RefreshCommand = new RelayCommand(Reload);
        OpenFolderCommand = new RelayCommand(() => Ui.OpenFolder(AppPaths.Backups));
        _s.ChangesApplied += () => System.Windows.Application.Current.Dispatcher.Invoke(Reload);
        Reload();
    }

    public ICommand RollbackCommand { get; }
    public ICommand RollbackAllCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ObservableCollection<ChangeSetVm> Sets { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public void Reload()
    {
        Sets.Clear();
        foreach (var s in _s.Store.Load().OrderByDescending(s => s.CreatedUtc))
        {
            Sets.Add(new ChangeSetVm(s));
        }

        History.Clear();
        foreach (var h in _s.History.Entries.OrderByDescending(h => h.Time).Take(200))
        {
            History.Add(h);
        }
    }

    private async Task RollbackOneAsync(ChangeSetVm? vm)
    {
        if (vm is null || !vm.CanRollback)
        {
            return;
        }

        var conflicts = _s.Store.LaterConflicts(vm.Set.Id);
        var warn = conflicts.Count > 0
            ? $"\n\nВнимание: позже эти же настройки меняли: {string.Join(", ", conflicts.Select(c => c.Title))}. Лучше сначала откатить их (или нажать «Откатить всё»)."
            : string.Empty;
        if (!Ui.Confirm($"Откатить «{vm.Title}»?\n\n{vm.Details}{warn}"))
        {
            return;
        }

        if (GameOrSteamFileInvolved(vm.Set) is { } blocker)
        {
            Ui.Info(blocker);
            return;
        }

        IReadOnlyList<string> messages;
        if (vm.Set.RequiresAdminToRollback && !Elevation.IsElevated)
        {
            var result = await Task.Run(() => AppServices.RunElevated(new ElevatedRequest { Mode = "rollback", ChangeSetIds = { vm.Set.Id } }));
            messages = result?.Messages ?? new List<string> { "Запрос прав администратора отклонён — ничего не изменено." };
        }
        else
        {
            messages = (await Task.Run(() => _s.Executor.Rollback(vm.Set.Id))).Messages;
        }

        _s.History.Add(new HistoryEntry(DateTime.Now, "Откат", vm.Title, string.Join("; ", messages)));
        Status = string.Join("\n", messages);
        Reload();
    }

    private async Task RollbackAllAsync()
    {
        if (!Ui.Confirm("Откатить ВСЕ изменения, сделанные программой (от новых к старым)?"))
        {
            return;
        }

        var active = _s.Store.Active();
        if (active.Select(GameOrSteamFileInvolved).FirstOrDefault(b => b is not null) is { } blocker)
        {
            Ui.Info(blocker);
            return;
        }

        var messages = new List<string>();
        var adminIds = active.Where(a => a.RequiresAdminToRollback).OrderByDescending(a => a.CreatedUtc).Select(a => a.Id).ToList();
        if (adminIds.Count > 0 && !Elevation.IsElevated)
        {
            // Only admin-level sets go to the elevated copy: if UAC is confirmed by another administrator account,
            // that process has a different HKCU, so per-user values must be restored here.
            var result = await Task.Run(() => AppServices.RunElevated(new ElevatedRequest { Mode = "rollback", ChangeSetIds = adminIds }));
            messages.AddRange(result?.Messages ?? new List<string> { "Права администратора не выданы — системные изменения не откачены." });
        }

        messages.AddRange((await Task.Run(() => _s.Executor.RollbackAll(includeAdmin: Elevation.IsElevated))).Messages);

        _s.History.Add(new HistoryEntry(DateTime.Now, "Откат", "Откатить всё", string.Join("; ", messages)));
        Status = string.Join("\n", messages);
        Reload();
    }

    /// <summary>Restoring video.txt or localconfig.vdf while the game/Steam runs would be overwritten by them on exit.</summary>
    private static string? GameOrSteamFileInvolved(ChangeSet set)
    {
        var files = set.Records.Where(r => r.Kind == BackupKind.File && !set.RolledBack).Select(r => r.Path ?? string.Empty).ToList();
        if (files.Any(f => f.EndsWith("video.txt", StringComparison.OrdinalIgnoreCase)) && Core.SystemInfo.SystemScanner.IsProcessRunning("dota2"))
        {
            return "Закройте Dota 2: иначе при выходе игра перезапишет восстановленный video.txt.";
        }

        if (files.Any(f => f.EndsWith("localconfig.vdf", StringComparison.OrdinalIgnoreCase)) && Core.SystemInfo.SystemScanner.IsProcessRunning("steam"))
        {
            return "Закройте Steam полностью: иначе он перезапишет восстановленный localconfig.vdf.";
        }

        return null;
    }
}

public sealed class AsyncCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private bool _running;

    public AsyncCommand(Func<T?, Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        _running = true;
        try
        {
            await _execute(parameter is T t ? t : default);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка команды", ex);
            System.Windows.MessageBox.Show(ex.Message, "Dota 2 Optimizer");
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public sealed class AboutViewModel : ObservableObject
{
    private string _logText = string.Empty;

    public AboutViewModel()
    {
        OpenLogsCommand = new RelayCommand(() => Ui.OpenFolder(AppPaths.Logs));
        RefreshLogCommand = new RelayCommand(LoadLog);
        LoadLog();
    }

    public ICommand OpenLogsCommand { get; }
    public ICommand RefreshLogCommand { get; }

    public string Version => "Версия " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");
    public string DataFolder => AppPaths.Root;

    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    private void LoadLog()
    {
        try
        {
            if (!File.Exists(Log.CurrentFile))
            {
                LogText = "Лог за сегодня пуст.";
                return;
            }

            using var fs = new FileStream(Log.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var lines = reader.ReadToEnd().Split('\n');
            LogText = string.Join('\n', lines.TakeLast(300));
        }
        catch (IOException ex)
        {
            LogText = ex.Message;
        }
    }
}

public sealed class MainViewModel
{
    public MainViewModel()
    {
        var services = new AppServices();
        Wizard = new WizardViewModel(services);
        Crash = new CrashViewModel(services);
        Network = new NetworkViewModel(services);
        Benchmark = new BenchmarkViewModel(services);
        Rollback = new RollbackViewModel(services);
        About = new AboutViewModel();
    }

    public WizardViewModel Wizard { get; }
    public CrashViewModel Crash { get; }
    public NetworkViewModel Network { get; }
    public BenchmarkViewModel Benchmark { get; }
    public RollbackViewModel Rollback { get; }
    public AboutViewModel About { get; }
}
