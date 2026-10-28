using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>A game card on the home page.</summary>
public sealed class HomeGameVm : ObservableObject
{
    private string _state = "Не проверено";
    private bool _found;

    public HomeGameVm(GameWorkspace ws)
    {
        Workspace = ws;
    }

    public GameWorkspace Workspace { get; }
    public string Name => Workspace.Name;
    public string Monogram => Workspace.Monogram;
    public string Genre => Workspace.Genre;
    public bool IsCs2 => Workspace.Game.Id == Core.Game.GameId.Cs2;

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public bool Found
    {
        get => _found;
        set => Set(ref _found, value);
    }
}

/// <summary>
/// Home page: the PC at a glance and one «Оптимизировать» button that applies, for every installed game, only what the
/// game pages would apply by default — recommended, undisputed, no reboot. Nothing is closed, deleted or overclocked.
/// </summary>
public sealed class HomeViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly Action<GameWorkspace, int> _open;
    private readonly DispatcherTimer _boostTimer;
    private readonly List<string> _lastRun = new();
    private bool _busy;
    private string _status = "Перед применением программа покажет список изменений.";
    private bool _boostEnabled;
    private string _boostState = string.Empty;
    private string _tempText = "Подсчитываю…";
    private TempSummary? _temp;
    private string _gtaState = "Режимы графики, редуксы, диагностика";

    public HomeViewModel(AppServices services, GameWorkspace dota, GameWorkspace cs2, Action<GameWorkspace, int> open, Action openGta, Action openPc)
    {
        _s = services;
        _open = open;
        Games.Add(new HomeGameVm(dota));
        Games.Add(new HomeGameVm(cs2));
        OptimizeCommand = new AsyncCommand(OptimizeAsync, () => !Busy);
        UndoAllCommand = new AsyncCommand(UndoAllAsync, () => !Busy && Report.Any(r => r.CanUndo));
        UndoLineCommand = new AsyncCommand<ReportLine>(UndoLineAsync);
        OpenGameCommand = new RelayCommand(p =>
        {
            if (p is HomeGameVm g)
            {
                _open(g.Workspace, MainViewModel.OptimizeTab);
            }
        });
        OpenRollbackCommand = new RelayCommand(() => _open(dota, MainViewModel.RollbackTab));
        OpenGtaCommand = new RelayCommand(openGta);
        OpenPcCommand = new RelayCommand(openPc);
        RefreshBackgroundCommand = new AsyncCommand(RefreshBackgroundAsync);
        CleanTempCommand = new AsyncCommand(CleanTempAsync, () => _temp is { Files: > 0 });

        _s.SnapshotChanged += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(RaisePc);
        _s.ChangesApplied += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshUndo);

        _boostEnabled = !File.Exists(AppPaths.SessionBoostOff);
        // A plan left switched by a crash is put back when no game is running.
        if (RunningGame() is null && _s.Boost.Restore() is { } restored)
        {
            Log.Info(restored);
        }

        _boostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _boostTimer.Tick += (_, _) => TickBoost();
        _boostTimer.Start();
        UpdateBoostState(null);

        _ = RefreshBackgroundAsync();
        _ = MeasureTempAsync();
    }

    public ObservableCollection<HomeGameVm> Games { get; } = new();
    public ObservableCollection<ReportLine> Report { get; } = new();
    public ObservableCollection<RunningApp> Background { get; } = new();

    public ICommand OptimizeCommand { get; }
    public ICommand UndoAllCommand { get; }
    public ICommand UndoLineCommand { get; }
    public ICommand OpenGameCommand { get; }
    public ICommand OpenRollbackCommand { get; }
    public ICommand OpenGtaCommand { get; }
    public ICommand OpenPcCommand { get; }
    public ICommand RefreshBackgroundCommand { get; }
    public ICommand CleanTempCommand { get; }

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

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool HasReport => Report.Count > 0;
    public bool HasBackground => Background.Count > 0;

    public bool Scanned => _s.Snapshot is not null;
    public string ProfileText => _s.Snapshot?.Profile switch
    {
        PcProfile.Weak => "Слабый ПК",
        PcProfile.Medium => "Средний ПК",
        PcProfile.Strong => "Мощный ПК",
        _ => "ПК ещё не проверен",
    };

    public string GpuText => _s.Snapshot?.PrimaryGpu?.Name ?? "Первое нажатие «Оптимизировать» просканирует ПК (около минуты)";
    public string CpuText => _s.Snapshot?.Cpu is { } c ? $"{c.Name} · {c.Cores} ядер / {c.LogicalProcessors} потоков" : "—";
    public string RamText => _s.Snapshot?.Memory is { } m ? $"{m.TotalGb:0} ГБ ОЗУ" + (m.LikelyDualChannel == false ? " · одноканальный режим" : string.Empty) : "—";
    public string DisplayText => _s.Snapshot?.Display is { } d ? (d.RefreshHz > 0 ? $"{d.Width}×{d.Height} · {d.RefreshHz} Гц" : $"{d.Width}×{d.Height}") : "—";

    public bool BoostEnabled
    {
        get => _boostEnabled;
        set
        {
            if (!Set(ref _boostEnabled, value))
            {
                return;
            }

            try
            {
                if (value)
                {
                    File.Delete(AppPaths.SessionBoostOff);
                }
                else
                {
                    Directory.CreateDirectory(AppPaths.Root);
                    File.WriteAllText(AppPaths.SessionBoostOff, "1");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Не удалось сохранить настройку ускорения: {ex.Message}");
            }

            if (!value)
            {
                _s.Boost.Restore();
            }

            UpdateBoostState(null);
        }
    }

    public string BoostState
    {
        get => _boostState;
        private set => Set(ref _boostState, value);
    }

    public string TempText
    {
        get => _tempText;
        private set => Set(ref _tempText, value);
    }

    private void RaisePc()
    {
        foreach (var n in new[] { nameof(Scanned), nameof(ProfileText), nameof(GpuText), nameof(CpuText), nameof(RamText), nameof(DisplayText) })
        {
            OnPropertyChanged(n);
        }

        if (_s.Snapshot is { } snap)
        {
            foreach (var g in Games)
            {
                var steam = snap.For(g.Workspace.Game.Id);
                g.Found = steam.GameFound;
                g.State = steam.GameFound ? steam.GameRunning ? "Установлена · запущена" : "Установлена" : "Не найдена";
            }
        }
    }

    /// <summary>GTA V is found outside Steam, so its line on the games card is filled separately.</summary>
    public string GtaState
    {
        get => _gtaState;
        private set => Set(ref _gtaState, value);
    }

    /// <summary>Called when the window shows: picks up the saved scan, so the page is filled without scanning again.</summary>
    public async Task LoadAsync()
    {
        _ = FindGtaAsync();
        if (_s.Snapshot is null && !_s.QuickScanTried)
        {
            _s.QuickScanTried = true;
            var quick = await Task.Run(_s.QuickScan);
            if (quick is not null && _s.Snapshot is null)
            {
                _s.Snapshot = quick;
            }
        }

        RaisePc();
    }

    private async Task FindGtaAsync()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var gta = await Task.Run(() => new Core.GtaRp.GtaLocator(_s.Fs, _s.Registry, documents).Locate(SystemScanner.IsProcessRunning));
            GtaState = gta.GameFound
                ? $"GTA V найдена · {gta.EditionRu}" + (gta.RageMpDir is null ? string.Empty : " · RAGE MP")
                : "GTA V не найдена · режимы работают через settings.xml";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Поиск GTA V: {ex.Message}");
        }
    }

    private async Task OptimizeAsync()
    {
        Busy = true;
        Report.Clear();
        _lastRun.Clear();
        RaiseReport();
        try
        {
            var snapshot = _s.Snapshot;
            if (snapshot is null)
            {
                Status = "Сканирую ПК… Это нужно один раз, результат сохранится.";
                snapshot = await Task.Run(_s.Rescan);
                _s.Snapshot = snapshot;
            }

            Status = "Подбираю настройки…";
            var plans = new List<(string Game, TweakPlan Plan)>();
            var blocked = new List<(string Game, TweakPlan Plan)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in Games)
            {
                var (apply, stop) = await g.Workspace.Wizard.OneClickPlanAsync(_s.Snapshot ?? snapshot);
                // Windows-wide settings share their id across games: apply them once.
                plans.AddRange(apply.Where(p => seen.Add(p.Tweak.Id)).Select(p => (g.Name, p)));
                blocked.AddRange(stop.Where(p => !seen.Contains(p.Tweak.Id)).Select(p => (g.Name, p)));
            }

            if (plans.Count == 0)
            {
                foreach (var (game, p) in blocked)
                {
                    Say($"⚠ {game}: {p.Tweak.Title} — {p.StatusText}");
                }

                Status = blocked.Count == 0
                    ? "Всё рекомендованное уже настроено. Остальное — по желанию на страницах игр."
                    : "Сейчас нечего применить автоматически — см. причины ниже.";
                return;
            }

            var list = string.Join("\n", plans.Select(p => $"• {p.Plan.Tweak.Title}"));
            var skipped = blocked.Count == 0 ? string.Empty : $"\n\nПропущено сейчас: {string.Join(", ", blocked.Select(b => b.Plan.Tweak.Title))} (причины — в отчёте).";
            if (!Ui.Confirm($"Оптимизировать? Будет изменено ({plans.Count}):\n\n{list}{skipped}\n\nПрограммы не закрываются, ничего не удаляется и не разгоняется. Перед каждым шагом сохраняется копия — всё отменяется кнопкой «Отменить всё».",
                    "Оптимизация"))
            {
                Status = "Отменено — ничего не изменено.";
                return;
            }

            Status = "Применяю…";
            var lines = await _s.ApplyPlansAsync(plans.Select(p => p.Plan).ToList(), createRestorePoint: plans.Any(p => p.Plan.RequiresAdmin));
            foreach (var line in lines)
            {
                Say(line.Text, line.ChangeSetId);
                if (line.ChangeSetId is not null)
                {
                    _lastRun.Add(line.ChangeSetId);
                }
            }

            foreach (var (game, p) in blocked)
            {
                Say($"⚠ {game}: {p.Tweak.Title} — {p.StatusText}");
            }

            var ok = lines.Count(l => l.ChangeSetId is not null);
            Status = ok == 0
                ? "Ничего не применено — см. отчёт."
                : $"Готово: применено {ok}. Чтобы проверить эффект, запишите матч в «Мониторе матча». Остальные настройки — на страницах игр.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Оптимизация в один клик", ex);
            Status = $"Ошибка: {ex.Message}. Подробности — в логе («О программе»).";
        }
        finally
        {
            Busy = false;
            RaiseReport();
        }

        _s.NotifyChangesApplied();
    }

    private void Say(string text, string? id = null)
    {
        Report.Add(new ReportLine(text, id));
        RaiseReport();
    }

    private void RaiseReport()
    {
        OnPropertyChanged(nameof(HasReport));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshUndo()
    {
        foreach (var line in Report.Where(l => l.ChangeSetId is not null))
        {
            var active = _s.Store.Find(line.ChangeSetId!) is { RolledBack: false };
            if (line.CanUndo && !active)
            {
                line.Text += " — отменено";
            }

            line.CanUndo = active;
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private async Task UndoAllAsync()
    {
        Busy = true;
        try
        {
            if (await _s.UndoManyAsync(_lastRun.ToList(), "Оптимизация в один клик") is { } messages)
            {
                Status = messages.Count == 0 ? "Нечего отменять." : "Оптимизация отменена: всё возвращено как было.";
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task UndoLineAsync(ReportLine? line)
    {
        if (line?.ChangeSetId is { } id && await _s.UndoAsync(id) is not null)
        {
            Status = "Изменение отменено.";
        }
    }

    private static string? RunningGame() =>
        Core.Game.Games.All.FirstOrDefault(g => SystemScanner.IsProcessRunning(g.ProcessName))?.Name;

    private void TickBoost()
    {
        if (!BoostEnabled)
        {
            return;
        }

        var game = RunningGame();
        string? message;
        try
        {
            message = _s.Boost.Update(game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Ускорение на время игры: {ex.Message}");
            return;
        }

        UpdateBoostState(message is null ? null : game);
        if (message is not null)
        {
            Status = message;
        }
    }

    private void UpdateBoostState(string? game) =>
        BoostState = !BoostEnabled
            ? "Выключено."
            : _s.Boost.Active
                ? $"Сейчас действует{(game is null ? string.Empty : $" — {game}")}: план «Высокая производительность»."
                : "Ждёт запуска Dota 2 или CS2. Работает, пока FrameLab открыт (можно свернуть).";

    /// <summary>Restores the power plan when the app closes during a game.</summary>
    public void Shutdown()
    {
        _boostTimer.Stop();
        _s.Boost.Restore();
    }

    private async Task RefreshBackgroundAsync()
    {
        var running = await Task.Run(BackgroundApps.FindRunning);
        Background.Clear();
        foreach (var r in running.Where(r => r.App.Category != "Steam"))
        {
            Background.Add(r);
        }

        OnPropertyChanged(nameof(HasBackground));
    }

    private async Task MeasureTempAsync()
    {
        _temp = await Task.Run(() => TempCleaner.Measure(TempCleaner.UserTemp, DateTime.UtcNow));
        TempText = _temp.Files == 0 ? "Старых временных файлов нет." : $"{_temp.SizeText} в {_temp.Files} старых файлах";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task CleanTempAsync()
    {
        if (_temp is not { Files: > 0 } t ||
            !Ui.Confirm($"Удалить временные файлы старше суток ({t.SizeText})?\n\nЭто то же, что «Очистка диска → Временные файлы». Файлы, которые сейчас используются, пропускаются. Удаление не отменяется, но эти файлы программам больше не нужны."))
        {
            return;
        }

        Busy = true;
        try
        {
            var done = await Task.Run(() => TempCleaner.Clean(TempCleaner.UserTemp, DateTime.UtcNow));
            Status = $"Освобождено {done.SizeText} (файлов: {done.Files}).";
            _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Очистка", "Временные файлы", Status));
        }
        finally
        {
            Busy = false;
        }

        await MeasureTempAsync();
    }
}
