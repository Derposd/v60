using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.GtaRp;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>One graphics mode card. Only one mode is applied at a time: two writes of settings.xml would overwrite each other.</summary>
public sealed class GtaModeVm : ObservableObject
{
    private TweakPlan? _plan;
    private bool _isSelected;
    private bool _isRecommended;
    private bool _isActive;

    public GtaModeVm(GtaMode mode)
    {
        Mode = mode;
    }

    public GtaMode Mode { get; }
    public string Id => Mode.Id;
    public string Title => Mode.Title;
    public string Tagline => Mode.Tagline;
    public string Explanation => Mode.Explanation;
    public bool IsRoleplay => Mode.Kind == GtaModeKind.Roleplay;
    public Effectiveness EffectValue => Mode.Effect;

    /// <summary>For the PC modes the effect is extra FPS; for the roleplay ones it is how much the picture costs.</summary>
    public string Effect => IsRoleplay && Mode.Effect == Effectiveness.HardwareDependent
        ? "Нагрузка: зависит от железа"
        : "Прирост FPS: " + Mode.Effect.Ru();

    public object Icon => System.Windows.Application.Current.Resources[Mode.Id switch
    {
        GtaModes.Potato => "IconCpu",
        GtaModes.Weak => "IconSliders",
        GtaModes.Medium => "IconMonitor",
        GtaModes.Pretty => "IconStar",
        _ => "IconBolt",
    }];

    public TweakPlan? Plan
    {
        get => _plan;
        set
        {
            if (Set(ref _plan, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public string StatusText => Plan?.StatusText ?? "Проверяю…";
    public bool CanApply => Plan?.Status == TweakStatus.CanApply;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsRecommended
    {
        get => _isRecommended;
        set => Set(ref _isRecommended, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}

/// <summary>A roleplay project the player picked; only changes which hints are shown.</summary>
public sealed record GtaProjectInfo(string Id, string Name, string Text);

/// <summary>
/// The GTA RP section: graphics modes, the other GTA / Windows tweaks, the redux manager and the diagnosis.
/// GTA V is not a Steam-only game, so it lives beside the Dota 2 / CS2 workspaces instead of being one of them.
/// </summary>
public sealed class GtaViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly Dictionary<string, bool> _userChoices = new();
    private GtaInfo _gta = new();
    private IReadOnlyList<Tweak> _tweaks = Array.Empty<Tweak>();
    private SystemSnapshot? _preparedFor;
    private bool _busy;
    private bool _stale = true;
    private string _status = "Откройте раздел, чтобы проверить GTA V.";
    private string _recommendTitle = "Подбираю режим…";
    private string _recommendReason = string.Empty;
    private string _activeText = string.Empty;
    private GtaModeVm? _selected;
    private bool _hasActiveMode;
    private GtaProjectInfo _project;

    public GtaViewModel(AppServices services)
    {
        _s = services;
        Redux = new ReduxViewModel(services, () => _gta);
        foreach (var m in GtaModes.All)
        {
            var vm = new GtaModeVm(m);
            (m.Kind == GtaModeKind.Roleplay ? RoleplayModes : PerformanceModes).Add(vm);
        }

        Projects = new[]
        {
            new GtaProjectInfo("gta5rp", "GTA5RP",
                "Сервера проекта работают поверх вашей копии GTA V: режимы графики, кэш RAGE MP и редуксы действуют так же, как для одиночной игры. " +
                "Какие моды разрешены — смотрите в правилах на официальном сайте и форуме проекта: приложение их не знает и не проверяет."),
            new GtaProjectInfo("majestic", "Majestic RP",
                "Проект тоже запускает вашу GTA V со своим клиентом, а графику берёт из того же settings.xml — режимы применяются и для него. " +
                "Правила по модам и редуксам у проекта свои: прочитайте их на официальном сайте или в Discord проекта до установки пака."),
        };
        _project = Projects[0];

        RefreshCommand = new AsyncCommand(() => RefreshAsync(rescan: false), () => !Busy);
        RescanCommand = new AsyncCommand(() => RefreshAsync(rescan: true), () => !Busy);
        SelectModeCommand = new RelayCommand(p => SelectMode(p as GtaModeVm));
        ApplyModeCommand = new AsyncCommand(ApplyModeAsync, () => !Busy && SelectedMode is not null);
        UndoModeCommand = new AsyncCommand(UndoModeAsync, () => !Busy && ModeChangeIds().Count > 0);
        ApplyTweaksCommand = new AsyncCommand(ApplyTweaksAsync, () => !Busy && Tweaks.Any(t => t.IsSelected));
        UndoTweakCommand = new AsyncCommand<TweakItemVm>(UndoTweakAsync);
        UndoLineCommand = new AsyncCommand<ReportLine>(UndoLineAsync);
        CopyCommand = new RelayCommand(p =>
        {
            if (p is string text)
            {
                System.Windows.Clipboard.SetText(text);
            }
        });
        OpenLinkCommand = new RelayCommand(p =>
        {
            if (p is string url)
            {
                Ui.OpenUrl(url);
            }
        });
        OpenSettingsFolderCommand = new RelayCommand(() =>
        {
            if (Path.GetDirectoryName(_gta.SettingsPath) is { Length: > 0 } dir)
            {
                Ui.OpenFolder(dir);
            }
        });

        _s.ChangesApplied += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _stale = true;
            if (IsActive)
            {
                _ = RefreshAsync(rescan: false);
            }
        });
        _s.SnapshotChanged += () => _stale = true;
    }

    public ReduxViewModel Redux { get; }

    public ObservableCollection<GtaModeVm> PerformanceModes { get; } = new();
    public ObservableCollection<GtaModeVm> RoleplayModes { get; } = new();
    public IEnumerable<GtaModeVm> Modes => PerformanceModes.Concat(RoleplayModes);
    public ObservableCollection<TweakItemVm> Tweaks { get; } = new();
    public ObservableCollection<ReportLine> Report { get; } = new();
    public ObservableCollection<Advice> Diagnosis { get; } = new();
    public IReadOnlyList<GtaProjectInfo> Projects { get; }

    public ICommand RefreshCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand SelectModeCommand { get; }
    public ICommand ApplyModeCommand { get; }
    public ICommand UndoModeCommand { get; }
    public ICommand ApplyTweaksCommand { get; }
    public ICommand UndoTweakCommand { get; }
    public ICommand UndoLineCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand OpenLinkCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }

    /// <summary>True while the GTA RP section is the one shown.</summary>
    public bool IsActive { get; set; }

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

    public GtaInfo Info => _gta;

    public string GameText => _gta.GameFound ? $"{_gta.GameDir}" : "Не найдена";
    public string EditionText => _gta.GameFound ? _gta.EditionRu : "Режимы всё равно работают через «Документы»";
    public string SettingsText => _gta.SettingsExists ? "settings.xml найден" : "Ещё не создан";
    public string SettingsPathText => _gta.SettingsPath;
    public string RageMpText => _gta.RageMpDir ?? "Не найден";
    public string RageMpSub => _gta.RageMpDir is null ? "Нужен только для сброса кэша" : "Клиент мультиплеера";
    public bool GameFound => _gta.GameFound;
    public bool SettingsFound => _gta.SettingsExists;
    public bool RageMpFound => _gta.RageMpDir is not null;
    public bool GameRunning => _gta.AnyRunning;

    public string RunningText => _gta.RageMpRunning
        ? "Клиент RAGE MP запущен. Закройте его и GTA V: при выходе игра перезапишет settings.xml, и изменения пропадут."
        : "GTA V запущена. Закройте игру: при выходе она перезапишет settings.xml, и изменения пропадут.";

    public string RecommendTitle
    {
        get => _recommendTitle;
        private set => Set(ref _recommendTitle, value);
    }

    public string RecommendReason
    {
        get => _recommendReason;
        private set => Set(ref _recommendReason, value);
    }

    public bool HasActiveMode
    {
        get => _hasActiveMode;
        private set => Set(ref _hasActiveMode, value);
    }

    /// <summary>Which mode the settings file holds right now.</summary>
    public string ActiveText
    {
        get => _activeText;
        private set => Set(ref _activeText, value);
    }

    public GtaModeVm? SelectedMode
    {
        get => _selected;
        private set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedText));
                OnPropertyChanged(nameof(SelectedStatus));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SelectedText => SelectedMode is null ? "Выберите режим" : $"Выбран режим «{SelectedMode.Title}»";
    public string SelectedStatus => SelectedMode?.StatusText ?? "Нажмите на карточку режима.";

    public bool HasModeChanges => ModeChangeIds().Count > 0;
    public bool HasReport => Report.Count > 0;
    public bool HasTweaks => Tweaks.Count > 0;

    public GtaProjectInfo Project
    {
        get => _project;
        set => Set(ref _project, value);
    }

    public bool IsGta5Rp
    {
        get => Project.Id == "gta5rp";
        set
        {
            if (value)
            {
                Project = Projects[0];
                OnPropertyChanged(nameof(IsMajestic));
            }
        }
    }

    public bool IsMajestic
    {
        get => Project.Id == "majestic";
        set
        {
            if (value)
            {
                Project = Projects[1];
                OnPropertyChanged(nameof(IsGta5Rp));
            }
        }
    }

    /// <summary>Called when the section is shown: scans the PC if nothing was scanned yet, then re-reads GTA V.</summary>
    public async Task ActivateAsync()
    {
        IsActive = true;
        if (!_stale && ReferenceEquals(_s.Snapshot, _preparedFor))
        {
            return;
        }

        await RefreshAsync(rescan: false);
    }

    private void SelectMode(GtaModeVm? mode)
    {
        if (mode is null)
        {
            return;
        }

        foreach (var m in Modes)
        {
            m.IsSelected = m == mode;
        }

        SelectedMode = mode;
    }

    private async Task RefreshAsync(bool rescan)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            var snapshot = _s.Snapshot;
            if (snapshot is null || rescan)
            {
                if (!rescan && !_s.QuickScanTried)
                {
                    _s.QuickScanTried = true;
                    Status = "Загружаю прошлое сканирование…";
                    snapshot = await Task.Run(_s.QuickScan);
                }

                if (snapshot is null || rescan)
                {
                    Status = "Сканирую ПК: видеокарта, память, диски… Обычно это 10–30 секунд.";
                    snapshot = await Task.Run(() => rescan ? _s.Rescan() : _s.Scan());
                }

                _s.Snapshot = snapshot;
            }

            Status = "Проверяю GTA V…";
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var gta = await Task.Run(() => new GtaLocator(_s.Fs, _s.Registry, documents).Locate(SystemScanner.IsProcessRunning));
            var settingsText = gta.SettingsExists ? await Task.Run(() => TryRead(gta.SettingsPath)) : null;
            _gta = gta;
            _tweaks = GtaCatalog.Create(gta);

            var ctx = new TweakContext { Snapshot = snapshot, Registry = _s.Registry, FileSystem = _s.Fs, Commands = _s.Commands };
            var plans = await Task.Run(() => GtaCatalog.PlanAll(ctx, _tweaks));
            ShowModes(snapshot, plans, settingsText);
            ShowTweaks(plans);

            var health = await Task.Run(() => new HealthChecks(_s.Commands).RunGeneral(snapshot));
            var background = await Task.Run(BackgroundApps.FindRunning);
            Diagnosis.Clear();
            foreach (var a in GtaAdvisor.Evaluate(gta, snapshot, plans, settingsText, health, background, DateTime.Today))
            {
                Diagnosis.Add(a);
            }

            _preparedFor = snapshot;
            _stale = false;
            RaiseInfo();
            Status = gta.GameFound
                ? $"GTA V найдена · проверено {DateTime.Now:HH:mm}. Ничего не меняется без вашего подтверждения."
                : "GTA V не найдена, но режимы графики можно применить, если в «Документах» есть settings.xml.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Проверка GTA V", ex);
            Status = "Не удалось проверить GTA V: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось прочитать {path}: {ex.Message}");
            return null;
        }
    }

    private void ShowModes(SystemSnapshot snapshot, IReadOnlyList<TweakPlan> plans, string? settingsText)
    {
        var (recommended, reason) = GtaModes.Recommend(snapshot);
        GtaMode? active = null;
        if (settingsText is not null)
        {
            try
            {
                active = GtaModes.Detect(GtaSettingsFile.Parse(settingsText), snapshot.PrimaryGpu);
            }
            catch (InvalidDataException)
            {
                // The advisor reports a broken file.
            }
        }

        foreach (var m in Modes)
        {
            m.Plan = plans.FirstOrDefault(p => p.Tweak.Id == GtaCatalog.ModePrefix + m.Id);
            m.IsRecommended = m.Id == recommended.Id;
            m.IsActive = active?.Id == m.Id || m.Plan?.Status == TweakStatus.AlreadyOptimal;
        }

        RecommendTitle = $"Рекомендуем для вашего ПК: «{recommended.Title}»";
        RecommendReason = reason;
        var on = Modes.FirstOrDefault(m => m.IsActive);
        HasActiveMode = on is not null;
        ActiveText = on is not null
            ? $"Сейчас включён режим «{on.Title}»."
            : settingsText is null
                ? "Файла settings.xml пока нет: запустите GTA V один раз и закройте — после этого режимы можно применять."
                : "Сейчас в игре ваши собственные настройки (не совпадают ни с одним режимом).";

        // Only the recommended mode is preselected; the player's own click is kept across refreshes.
        SelectMode(Modes.FirstOrDefault(m => m.Id == SelectedMode?.Id) ?? Modes.First(m => m.IsRecommended));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(HasModeChanges));
    }

    private void ShowTweaks(IReadOnlyList<TweakPlan> plans)
    {
        foreach (var t in Tweaks)
        {
            if (t.CanSelect && t.IsSelected != t.Plan.DefaultSelected)
            {
                _userChoices[t.Id] = t.IsSelected;
            }
        }

        Tweaks.Clear();
        foreach (var p in plans.Where(p => !p.Tweak.Id.StartsWith(GtaCatalog.ModePrefix, StringComparison.Ordinal)))
        {
            Tweaks.Add(new TweakItemVm(p, _userChoices.TryGetValue(p.Tweak.Id, out var chosen) ? chosen : p.DefaultSelected, _s.ActiveChangeFor(p.Tweak.Id)));
        }

        OnPropertyChanged(nameof(HasTweaks));
    }

    private void RaiseInfo()
    {
        foreach (var name in new[]
                 {
                     nameof(Info), nameof(GameText), nameof(EditionText), nameof(SettingsText), nameof(SettingsPathText), nameof(RageMpText),
                     nameof(RageMpSub), nameof(GameFound), nameof(SettingsFound), nameof(RageMpFound), nameof(GameRunning), nameof(RunningText),
                 })
        {
            OnPropertyChanged(name);
        }

        Redux.OnGtaChanged();
    }

    private List<string> ModeChangeIds() =>
        _s.Store.Active().Where(c => c.TweakId.StartsWith(GtaCatalog.ModePrefix, StringComparison.Ordinal)).Select(c => c.Id).ToList();

    private async Task ApplyModeAsync()
    {
        var mode = SelectedMode;
        if (mode is null)
        {
            return;
        }

        // The game may have been started or closed since the last check.
        await RefreshAsync(rescan: false);
        mode = Modes.First(m => m.Id == mode.Id);
        var plan = mode.Plan;
        if (plan is null || plan.Status != TweakStatus.CanApply)
        {
            Ui.Info(plan?.StatusText ?? "Режим пока недоступен.", $"Режим «{mode.Title}»");
            return;
        }

        if (!Ui.Confirm($"Применить режим «{mode.Title}»?\n\n{plan.StatusText}\n\nКопия settings.xml сохраняется: «Вернуть как было» восстановит ваши настройки. " +
                        "GTA V должна быть закрыта.", "GTA V — режим графики"))
        {
            return;
        }

        Busy = true;
        try
        {
            Report.Clear();
            foreach (var line in await _s.ApplyPlansAsync(new[] { plan }, createRestorePoint: false))
            {
                Report.Add(new ReportLine(line.Text, line.ChangeSetId));
            }

            OnPropertyChanged(nameof(HasReport));
        }
        finally
        {
            Busy = false;
        }

        _s.NotifyChangesApplied();
    }

    private async Task UndoModeAsync()
    {
        var ids = ModeChangeIds();
        if (ids.Count == 0)
        {
            Ui.Info("Программа ещё не меняла графику GTA V — возвращать нечего.");
            return;
        }

        var messages = await _s.UndoManyAsync(ids, "Графика GTA V: вернуть как было");
        if (messages is not null)
        {
            Report.Clear();
            foreach (var m in messages)
            {
                Report.Add(new ReportLine(m));
            }

            OnPropertyChanged(nameof(HasReport));
        }
    }

    private async Task ApplyTweaksAsync()
    {
        var selected = Tweaks.Where(t => t.IsSelected).Select(t => t.Plan).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var steps = string.Join("\n", selected.Select(p => "• " + p.Tweak.Title));
        if (!Ui.Confirm($"Применить выбранное?\n\n{steps}\n\nПеред каждым шагом сохраняется копия, всё можно отменить здесь или на вкладке «Откат»."))
        {
            return;
        }

        Busy = true;
        try
        {
            Report.Clear();
            foreach (var line in await _s.ApplyPlansAsync(selected, createRestorePoint: selected.Any(p => p.RequiresAdmin)))
            {
                Report.Add(new ReportLine(line.Text, line.ChangeSetId));
            }

            OnPropertyChanged(nameof(HasReport));
        }
        finally
        {
            Busy = false;
        }

        _s.NotifyChangesApplied();
    }

    private async Task UndoTweakAsync(TweakItemVm? item)
    {
        if (item?.Applied is { } set)
        {
            await _s.UndoAsync(set.Id);
        }
    }

    private async Task UndoLineAsync(ReportLine? line)
    {
        if (line?.ChangeSetId is { } id && await _s.UndoAsync(id) is not null)
        {
            line.CanUndo = false;
            line.Text += "  · отменено";
        }
    }
}
