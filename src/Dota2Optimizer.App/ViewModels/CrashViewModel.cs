using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.Core.Crash;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.App.ViewModels;

public sealed class CacheItemVm : ObservableObject
{
    private bool _isSelected;

    public CacheItemVm(CacheLocation location)
    {
        Location = location;
        _isSelected = location.Exists && location.SizeBytes > 0;
    }

    public CacheLocation Location { get; }
    public string Title => $"{Location.Name} — {(Location.Exists ? Ui.Size(Location.SizeBytes) : "нет")}";
    public string Path => Location.Path;
    public string Note => Location.Note;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class CrashViewModel : ObservableObject
{
    private readonly AppServices _s;
    private string _summary = "Нажмите «Анализировать»: будут прочитаны журналы Windows за 30 дней, дампы и логи игры. Ничего не изменяется.";
    private bool _busy;

    public CrashViewModel(AppServices services)
    {
        _s = services;
        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => !_busy);
        ScanCachesCommand = new AsyncCommand(ScanCachesAsync, () => !_busy);
        CleanCachesCommand = new AsyncCommand(CleanCachesAsync, () => !_busy && Caches.Any(c => c.IsSelected));
        ValidateCommand = new RelayCommand(Validate);
        FlushDownloadCacheCommand = new RelayCommand(FlushDownloadCache);
        OpenDumpFolderCommand = new RelayCommand(p =>
        {
            if (p is string path)
            {
                Ui.OpenFolder(System.IO.Path.GetDirectoryName(path)!);
            }
        });
        OpenLinkCommand = new RelayCommand(p =>
        {
            if (p is string url && url.StartsWith("https://", StringComparison.Ordinal))
            {
                Ui.OpenUrl(url);
            }
        });
    }

    public ICommand AnalyzeCommand { get; }
    public ICommand ScanCachesCommand { get; }
    public ICommand CleanCachesCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand FlushDownloadCacheCommand { get; }
    public ICommand OpenDumpFolderCommand { get; }
    public ICommand OpenLinkCommand { get; }

    public ObservableCollection<CrashFinding> Findings { get; } = new();
    public ObservableCollection<LogFinding> LogFindings { get; } = new();
    public ObservableCollection<DumpFile> Dumps { get; } = new();
    public ObservableCollection<RuntimeCheck> Runtimes { get; } = new();
    public ObservableCollection<CacheItemVm> Caches { get; } = new();
    public IReadOnlyList<Conflict> Conflicts => CrashTools.KnownConflicts;

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    private SteamInfo Steam => _s.Snapshot?.Steam ?? new SteamInfo();

    private async Task AnalyzeAsync()
    {
        _busy = true;
        Summary = "Анализирую журналы…";
        try
        {
            var steam = Steam;
            var events = await Task.Run(() => EventLogSource.Read(30));
            var findings = CrashClassifier.Classify(events);
            var dumps = await Task.Run(() => CrashTools.FindDumps(steam));
            var runtimes = await Task.Run(() => CrashTools.CheckRuntimes(_s.Registry));
            var logs = await Task.Run(() => CrashTools.LogFiles(steam)
                .SelectMany(f =>
                {
                    try
                    {
                        return LogAnalyzer.Analyze(CrashTools.ReadTail(f)).Select(x => x with { Title = $"{x.Title} ({System.IO.Path.GetFileName(f)})" });
                    }
                    catch (IOException)
                    {
                        return Enumerable.Empty<LogFinding>();
                    }
                })
                .ToList());

            Replace(Findings, findings);
            Replace(Dumps, dumps);
            Replace(Runtimes, runtimes);
            Replace(LogFindings, logs);

            var crashes = findings.Where(f => f.Severity == Severity.Critical).Sum(f => f.Count);
            var consoleLog = steam.DotaInstallDir is not null && File.Exists(System.IO.Path.Combine(steam.DotaInstallDir, "game", "dota", "console.log"));
            Summary = (crashes == 0 ? "Серьёзных сбоев за 30 дней не найдено." : $"Найдено критичных событий: {crashes}. Самые частые причины — сверху.") +
                      (consoleLog ? string.Empty : " console.log не найден: добавьте параметр запуска -condebug, сыграйте до вылета и повторите анализ.");
            if (steam.SteamPath is null)
            {
                Summary += " Steam не найден — сначала выполните сканирование на вкладке «Оптимизация».";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ScanCachesAsync()
    {
        _busy = true;
        try
        {
            var vendors = _s.Snapshot?.RealGpus.Select(g => g.Vendor) ?? new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel };
            var steam = Steam;
            var caches = await Task.Run(() => CrashTools.ShaderCaches(steam, vendors));
            Replace(Caches, caches.Select(c => new CacheItemVm(c)));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CleanCachesAsync()
    {
        if (SystemScanner.IsProcessRunning("dota2"))
        {
            Ui.Info("Закройте Dota 2 перед очисткой кеша шейдеров.");
            return;
        }

        var selected = Caches.Where(c => c.IsSelected).Select(c => c.Location).ToList();
        if (!Ui.Confirm(
                $"Удалить кеш шейдеров ({selected.Count} папок, {Ui.Size(selected.Sum(c => c.SizeBytes))})?\n\n" +
                "Это лечит фризы и артефакты после обновления драйвера. Кеш не сохраняется в бэкап — он пересоздаётся сам, " +
                "но первые 1–2 матча могут подтормаживать, пока шейдеры компилируются заново."))
        {
            return;
        }

        _busy = true;
        try
        {
            var r = await Task.Run(() => CrashTools.Clean(selected));
            _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Обслуживание", "Очистка кеша шейдеров", $"Удалено файлов: {r.Deleted}, освобождено {Ui.Size(r.FreedBytes)}, пропущено (заняты): {r.Skipped}"));
            Ui.Info($"Удалено файлов: {r.Deleted}, освобождено {Ui.Size(r.FreedBytes)}.\nПропущено занятых файлов: {r.Skipped} (их использует запущенная программа — это нормально).");
        }
        finally
        {
            _busy = false;
        }

        await ScanCachesAsync();
    }

    private void Validate()
    {
        if (Ui.Confirm("Запустить проверку целостности файлов Dota 2 в Steam?\n\nSteam сверит файлы и докачает повреждённые. Это может занять несколько минут и потребовать трафика."))
        {
            CrashTools.StartValidate();
            _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Обслуживание", "Проверка целостности файлов", "steam://validate/570"));
        }
    }

    private void FlushDownloadCache()
    {
        if (Ui.Confirm("Очистить кеш загрузки Steam?\n\nЭто штатная функция Steam (Настройки → Загрузки → Очистить кеш). Помогает, если обновление зависло или не скачивается. Steam перезапустится и может попросить войти заново."))
        {
            CrashTools.FlushSteamDownloadCache();
            _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Обслуживание", "Очистка кеша загрузки Steam", "steam://flushconfig"));
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }
}
