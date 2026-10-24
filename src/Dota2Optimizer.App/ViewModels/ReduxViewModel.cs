using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.GtaRp;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Redux;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>A file of the checked pack, as shown in the lists.</summary>
public sealed record ReduxFileVm(string Path, string Detail);

/// <summary>An installed (or interrupted) pack.</summary>
public sealed class ReduxPackVm
{
    public ReduxPackVm(ReduxManifest manifest)
    {
        Manifest = manifest;
    }

    public ReduxManifest Manifest { get; }
    public string Name => Manifest.Name;
    public bool IsUnfinished => Manifest.State == "Installing";
    public string When => Manifest.InstalledAt.ToString("dd.MM.yyyy HH:mm");

    public string Details => IsUnfinished
        ? $"Установка прервалась ({When}): часть файлов могла остаться. Откатите её — оригиналы вернутся на место."
        : $"Установлен {When} · {Manifest.Entries.Count} файлов, {Ui.Size(Manifest.TotalBytes)} · заменено оригиналов: {Manifest.Entries.Count(e => e.Action == "Replaced")}";

    public string ButtonText => IsUnfinished ? "Откатить установку" : "Удалить (вернуть оригиналы)";
}

/// <summary>
/// Redux (graphics pack) manager page: pick the GTA V folder and a pack, check it, see exactly what will and will not be copied,
/// install with progress and cancel, and take installed packs out again. Code (ASI, DLL, scripts) is never installed.
/// </summary>
public sealed class ReduxViewModel : ObservableObject
{
    private const int ListLimit = 300;
    private readonly AppServices _s;
    private readonly Func<GtaInfo> _gta;
    private readonly ReduxManager _manager = new(ReduxManager.DefaultManifestDir);
    private readonly LatestProgress _progress = new();
    private readonly DispatcherTimer _progressTimer;
    private IReduxSource? _source;
    private ReduxPlan? _plan;
    private CancellationTokenSource? _cts;
    private string _gtaDir = string.Empty;
    private string _packPath = string.Empty;
    private bool _acceptUnknownRoot;
    private bool _busy;
    private bool _installing;
    private string _status = "Выберите папку GTA V и пак, затем нажмите «Проверить». До установки ничего не копируется.";
    private double _progressValue;
    private string _progressText = string.Empty;

    public ReduxViewModel(AppServices services, Func<GtaInfo> gta)
    {
        _s = services;
        _gta = gta;
        BrowseGtaCommand = new RelayCommand(BrowseGta, () => !Busy);
        FindGtaCommand = new RelayCommand(() => GtaDir = _gta().GameDir ?? GtaDir, () => !Busy && _gta().GameFound);
        BrowsePackFolderCommand = new RelayCommand(BrowsePackFolder, () => !Busy);
        BrowsePackArchiveCommand = new RelayCommand(BrowsePackArchive, () => !Busy);
        CheckCommand = new AsyncCommand(CheckAsync, () => !Busy && PackPath.Length > 0);
        InstallCommand = new AsyncCommand(InstallAsync, () => !Busy && _plan is { CanInstall: true } && (!_plan.NeedsRootConfirmation || AcceptUnknownRoot));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => Installing);
        ClearCommand = new RelayCommand(ClearPlan, () => !Busy && HasPlan);
        RemoveCommand = new AsyncCommand<ReduxPackVm>(RemoveAsync);
        OpenGtaFolderCommand = new RelayCommand(() => Ui.OpenFolder(GtaDir), () => Directory.Exists(GtaDir));

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _progressTimer.Tick += (_, _) => ShowProgress();
        LoadInstalled();
    }

    public ICommand BrowseGtaCommand { get; }
    public ICommand FindGtaCommand { get; }
    public ICommand BrowsePackFolderCommand { get; }
    public ICommand BrowsePackArchiveCommand { get; }
    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand OpenGtaFolderCommand { get; }

    public ObservableCollection<ReduxFileVm> InstallFiles { get; } = new();
    public ObservableCollection<ReduxFileVm> BlockedFiles { get; } = new();
    public ObservableCollection<ReduxFileVm> SkippedFiles { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<ReduxPackVm> Installed { get; } = new();

    public string GtaDir
    {
        get => _gtaDir;
        set
        {
            if (Set(ref _gtaDir, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(GtaDirState));
                OnPropertyChanged(nameof(GtaDirOk));
            }
        }
    }

    public bool GtaDirOk => GtaDir.Length > 0 && File.Exists(Path.Combine(GtaDir, GtaInfo.GameExeName));

    public string GtaDirState => GtaDir.Length == 0
        ? "Папка не выбрана"
        : GtaDirOk ? "GTA5.exe на месте" : "В этой папке нет GTA5.exe";

    public string PackPath
    {
        get => _packPath;
        set
        {
            if (Set(ref _packPath, value?.Trim().Trim('"') ?? string.Empty))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool AcceptUnknownRoot
    {
        get => _acceptUnknownRoot;
        set
        {
            if (Set(ref _acceptUnknownRoot, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool Installing
    {
        get => _installing;
        private set
        {
            if (Set(ref _installing, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => Set(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    public bool HasPlan => _plan is not null;
    public bool HasInstalled => Installed.Count > 0;
    public bool HasUnfinished => Installed.Any(p => p.IsUnfinished);
    public bool NeedsRootConfirmation => _plan?.NeedsRootConfirmation == true;
    public bool CanInstallPlan => _plan?.CanInstall == true;
    public string PlanTitle => _plan is null ? string.Empty : $"Пак «{_plan.Name}»";

    public string RootText => _plan?.RootKind switch
    {
        ReduxRootKind.Detected => _plan.PayloadPrefix.Length == 0
            ? "Структура пака совпадает с папкой GTA V (mods, update, x64…)."
            : $"Папка игры внутри пака найдена: {_plan.PayloadPrefix.TrimEnd('/')}",
        ReduxRootKind.Wrapper => $"Пак упакован в папку «{_plan.PayloadPrefix.TrimEnd('/')}» — её содержимое ляжет в папку GTA V.",
        ReduxRootKind.Unknown => "Не удалось понять, где в паке начинается папка игры.",
        _ => string.Empty,
    };

    public string InstallSummary => _plan is null ? string.Empty : $"Будет установлено: {_plan.InstallCount} файлов, {Ui.Size(_plan.InstallBytes)}";
    public int BlockedCount => _plan?.Blocked.Count() ?? 0;
    public int SkippedCount => _plan?.Skipped.Count() ?? 0;
    public string BlockedHeader => $"Не будет установлено — код или опасный путь ({BlockedCount})";
    public string SkippedHeader => $"Пропущено — документация, превью и лишние файлы ({SkippedCount})";
    public string InstallHeader => _plan is null ? string.Empty : $"Будет скопировано ({_plan.InstallCount})" + (_plan.InstallCount > ListLimit ? $" — показаны первые {ListLimit}" : string.Empty);

    /// <summary>Called by the GTA page after it located the game: fills the folder once, never overrides the player's choice.</summary>
    public void OnGtaChanged()
    {
        if (GtaDir.Length == 0 && _gta().GameDir is { } dir)
        {
            GtaDir = dir;
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void BrowseGta()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Папка с установленной GTA V (там, где GTA5.exe)" };
        if (Directory.Exists(GtaDir))
        {
            dlg.InitialDirectory = GtaDir;
        }

        if (dlg.ShowDialog() == true)
        {
            GtaDir = dlg.FolderName;
        }
    }

    private void BrowsePackFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Папка с распакованным редуксом" };
        if (dlg.ShowDialog() == true)
        {
            PackPath = dlg.FolderName;
        }
    }

    private void BrowsePackArchive()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Архив с редуксом",
            Filter = "Архивы (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            PackPath = dlg.FileName;
        }
    }

    private string StagingRoot() => GtaDirOk
        ? Path.Combine(GtaDir, ReduxManager.WorkFolder, "staging")
        : Path.Combine(AppPaths.Root, "redux-staging");

    private async Task CheckAsync()
    {
        ClearPlan();
        var path = PackPath;
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            Status = "Пак не найден: проверьте путь к папке или архиву.";
            return;
        }

        Busy = true;
        try
        {
            var archive = File.Exists(path) && !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);
            Status = archive
                ? "Распаковываю архив во временную папку… Для больших паков это несколько минут."
                : "Читаю список файлов пака…";
            var staging = StagingRoot();
            var source = await Task.Run(() => ReduxSources.Open(path, _s.Commands, _s.Fs, staging));
            var plan = await Task.Run(() => ReduxAnalyzer.Analyze(source));
            _source = source;
            _plan = plan;
            ShowPlan(plan);
            Status = plan.CanInstall
                ? "Проверено. Посмотрите, что будет установлено, и нажмите «Установить»."
                : "В паке нет файлов, которые можно безопасно установить.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Проверка редукса", ex);
            Status = "Не удалось открыть пак: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void ShowPlan(ReduxPlan plan)
    {
        foreach (var f in plan.ToInstall.Take(ListLimit))
        {
            InstallFiles.Add(new ReduxFileVm(f.GtaPath, Ui.Size(f.Length)));
        }

        foreach (var f in plan.Blocked)
        {
            BlockedFiles.Add(new ReduxFileVm(f.PackPath, f.Reason ?? "запрещённый тип файла"));
        }

        foreach (var f in plan.Skipped.Take(ListLimit))
        {
            SkippedFiles.Add(new ReduxFileVm(f.PackPath, f.Reason ?? "не нужен игре"));
        }

        foreach (var w in plan.Warnings)
        {
            Warnings.Add(w);
        }

        RaisePlan();
    }

    private void ClearPlan()
    {
        _source?.Dispose();
        _source = null;
        _plan = null;
        AcceptUnknownRoot = false;
        InstallFiles.Clear();
        BlockedFiles.Clear();
        SkippedFiles.Clear();
        Warnings.Clear();
        RaisePlan();
    }

    private void RaisePlan()
    {
        foreach (var name in new[]
                 {
                     nameof(HasPlan), nameof(NeedsRootConfirmation), nameof(CanInstallPlan), nameof(PlanTitle), nameof(RootText), nameof(InstallSummary),
                     nameof(BlockedCount), nameof(SkippedCount), nameof(BlockedHeader), nameof(SkippedHeader), nameof(InstallHeader),
                 })
        {
            OnPropertyChanged(name);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private static bool GameRunning() =>
        SystemScanner.IsProcessRunning(Path.GetFileNameWithoutExtension(GtaInfo.GameExeName)) ||
        SystemScanner.IsProcessRunning(Path.GetFileNameWithoutExtension(GtaInfo.RageMpExeName));

    private async Task InstallAsync()
    {
        if (_source is not { } source || _plan is not { } plan)
        {
            return;
        }

        if (!GtaDirOk)
        {
            Ui.Info("Укажите папку с установленной GTA V — ту, где лежит GTA5.exe.");
            return;
        }

        if (GameRunning())
        {
            Ui.Info("Закройте GTA V и клиент RAGE MP: файлы, которые игра использует, нельзя заменять.");
            return;
        }

        if (!Ui.Confirm($"Установить «{plan.Name}» в\n{GtaDir}?\n\n{InstallSummary}.\nЗаблокировано: {BlockedCount}, пропущено: {SkippedCount}.\n\n" +
                        "Заменяемые файлы игры переносятся в папку .framelab внутри GTA V и вернутся при удалении пака.\n\n" +
                        "Убедитесь, что правила вашего проекта разрешают этот пак.", "Установка редукса"))
        {
            return;
        }

        Busy = true;
        Installing = true;
        _cts = new CancellationTokenSource();
        _progress.Clear();
        ProgressValue = 0;
        ProgressText = "Начинаю…";
        _progressTimer.Start();
        try
        {
            var options = new ReduxInstallOptions { AcceptUnknownRoot = AcceptUnknownRoot, IsGameRunning = GameRunning };
            var gtaDir = GtaDir;
            var ct = _cts.Token;
            var result = await Task.Run(() => _manager.Install(source, plan, gtaDir, options, _progress, ct));
            Status = (result.Success ? "✓ " : "✗ ") + result.Message;
            _s.History.Add(new HistoryEntry(DateTime.Now, result.Success ? "Редукс установлен" : "Редукс не установлен", plan.Name, result.Message));
            if (result.Success)
            {
                ClearPlan();
            }
        }
        finally
        {
            _progressTimer.Stop();
            ShowProgress();
            Installing = false;
            Busy = false;
            _cts.Dispose();
            _cts = null;
            LoadInstalled();
        }
    }

    private void ShowProgress()
    {
        if (_progress.Latest is not { } p)
        {
            return;
        }

        ProgressValue = p.BytesTotal > 0 ? 100.0 * p.BytesDone / p.BytesTotal : 0;
        ProgressText = $"{p.FilesDone} из {p.FilesTotal} файлов · {Ui.Size(p.BytesDone)} из {Ui.Size(p.BytesTotal)}" + (p.Current is null ? string.Empty : $" · {p.Current}");
    }

    private async Task RemoveAsync(ReduxPackVm? pack)
    {
        if (pack is null || Busy)
        {
            return;
        }

        var question = pack.IsUnfinished
            ? $"Откатить прерванную установку «{pack.Name}»? Скопированные файлы удалятся, оригиналы вернутся на место."
            : $"Удалить «{pack.Name}»? Файлы пака удалятся, оригинальные файлы игры вернутся на место.";
        if (!Ui.Confirm(question, "Редуксы"))
        {
            return;
        }

        if (GameRunning())
        {
            Ui.Info("Закройте GTA V и клиент RAGE MP: файлы, которые игра использует, нельзя заменять.");
            return;
        }

        Busy = true;
        try
        {
            var result = await Task.Run(() => _manager.Uninstall(pack.Manifest.Id));
            var text = $"Возвращено оригиналов: {result.Restored}, удалено файлов пака: {result.Removed}.";
            Status = result.Success && result.Problems.Count == 0
                ? $"✓ «{pack.Name}» удалён. {text}"
                : $"⚠ «{pack.Name}»: {text} Проблемы ({result.Problems.Count}): {string.Join("; ", result.Problems.Take(3))}";
            _s.History.Add(new HistoryEntry(DateTime.Now, "Редукс удалён", pack.Name, Status));
        }
        finally
        {
            Busy = false;
            LoadInstalled();
        }
    }

    private void LoadInstalled()
    {
        Installed.Clear();
        try
        {
            foreach (var m in _manager.Installed().OrderByDescending(m => m.InstalledAt))
            {
                Installed.Add(new ReduxPackVm(m));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось прочитать список редуксов: {ex.Message}");
        }

        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(HasUnfinished));
    }

    /// <summary>Install reports progress for every chunk; the page shows the newest value a few times a second.</summary>
    private sealed class LatestProgress : IProgress<ReduxProgress>
    {
        private ReduxProgress? _latest;

        public ReduxProgress? Latest => Volatile.Read(ref _latest);

        public void Report(ReduxProgress value) => Volatile.Write(ref _latest, value);

        public void Clear() => Volatile.Write(ref _latest, null);
    }
}
