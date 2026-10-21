using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

public sealed record PassportRow(string Label, string Value);

public sealed class TweakItemVm : ObservableObject
{
    private bool _isSelected;

    public TweakItemVm(TweakPlan plan, bool selected, ChangeSet? applied)
    {
        Plan = plan;
        Applied = applied;
        _isSelected = selected && CanSelect;
    }

    /// <summary>Still-active change made by this tweak; drives the inline "Отменить" button.</summary>
    public ChangeSet? Applied { get; }
    public bool CanUndo => Applied is not null;
    public string? AppliedText => Applied is null ? null : $"Изменено программой {Applied.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}";

    public TweakPlan Plan { get; }
    public string Id => Plan.Tweak.Id;
    public string Title => Plan.Tweak.Title;
    public string Explanation => Plan.Tweak.Explanation;
    public string Effect => "Эффект: " + Plan.Tweak.Effectiveness.Ru();
    public string RecommendationLabel => Plan.RecommendedHere ? "Рекомендуется" : "По желанию";
    public bool IsRecommended => Plan.RecommendedHere;
    public bool IsDisputed => Plan.Tweak.Disputed;
    public string StatusText => Plan.StatusText;
    public string Risk => "Риск: " + Plan.Tweak.Risk;
    public string? ManualSteps => Plan.Tweak.ManualSteps;
    public string? CopyText => Plan.CopyText;
    public bool RequiresAdmin => Plan.RequiresAdmin;
    public bool RequiresRestart => Plan.Tweak.RequiresRestart;
    public bool CanSelect => Plan.Status == TweakStatus.CanApply;
    public TweakStatus Status => Plan.Status;
    public Effectiveness EffectValue => Plan.Tweak.Effectiveness;

    public string StatusLabel => Plan.Status switch
    {
        TweakStatus.AlreadyOptimal => "✓ Уже настроено",
        TweakStatus.CanApply => "Можно применить",
        TweakStatus.ManualOnly => "Вручную",
        TweakStatus.Blocked => "Сейчас недоступно",
        _ => "Не требуется",
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && CanSelect);
    }
}

public sealed class LaunchOptionVm : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public LaunchOptionVm(LaunchOptionInfo info, bool selected, Action changed)
    {
        Info = info;
        _isSelected = selected;
        _changed = changed;
    }

    public LaunchOptionInfo Info { get; }
    public string Token => Info.Token;
    public string Title => $"{Info.Token} — {Info.Title}";
    public string Explanation => Info.Explanation;
    public string Effect => "Эффект: " + Info.Effectiveness.Ru();
    public string RecommendationLabel => Info.Recommended ? "Рекомендуется" : "По желанию";
    public bool IsRecommended => Info.Recommended;
    public Effectiveness EffectValue => Info.Effectiveness;
    public bool IsDisputed => Info.Disputed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                _changed();
            }
        }
    }
}

/// <summary>One line of the apply report; lines for applied changes carry an inline undo.</summary>
public sealed class ReportLine : ObservableObject
{
    private string _text;
    private bool _canUndo;

    public ReportLine(string text, string? changeSetId = null)
    {
        _text = text;
        ChangeSetId = changeSetId;
        _canUndo = changeSetId is not null;
    }

    public string? ChangeSetId { get; }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool CanUndo
    {
        get => _canUndo;
        set => Set(ref _canUndo, value);
    }
}

/// <summary>Four-step wizard: scan → diagnosis → plan → apply.</summary>
public sealed class WizardViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly GameDefinition _target;
    private readonly Dictionary<string, bool> _userChoices = new();
    private SystemSnapshot? _preparedFor;
    private GameOptions _game = new();
    private int _step;
    private bool _busy;
    private string _status = "Нажмите «Сканировать», чтобы начать. Ничего не меняется без вашего подтверждения.";
    private bool _dryRun = true;
    private bool _createRestorePoint = true;
    private string _fpsMaxText = "60";
    private string? _mythsText;
    private string _apiText = string.Empty;
    private VendorGuide? _vendor;
    private bool _suppressReplan;

    public WizardViewModel(AppServices services, GameDefinition game)
    {
        _s = services;
        _target = game;
        ScanCommand = new AsyncCommand(ScanAsync, () => !Busy);
        NextCommand = new RelayCommand(() => Step++, () => Step < 3 && _s.Snapshot is not null && !Busy);
        BackCommand = new RelayCommand(() => Step--, () => Step > 0 && !Busy);
        GoToStepCommand = new RelayCommand(p => Step = int.Parse((string)p!, CultureInfo.InvariantCulture), p => _s.Snapshot is not null && !Busy);
        ApplyCommand = new AsyncCommand(ApplyAsync, () => !Busy && Tweaks.Any(t => t.IsSelected));
        OpenLinkCommand = new RelayCommand(p =>
        {
            if (p is string url && url.StartsWith("https://", StringComparison.Ordinal))
            {
                Ui.OpenUrl(url);
            }
        });
        CopyCommand = new RelayCommand(p =>
        {
            if (p is string text)
            {
                System.Windows.Clipboard.SetText(text);
                Status = "Строка скопирована в буфер обмена.";
            }
        });
        OpenPanelCommand = new RelayCommand(OpenVendorPanel, () => Vendor?.PanelCandidates.Count > 0);
        UndoTweakCommand = new AsyncCommand<TweakItemVm>(UndoTweakAsync);
        UndoReportLineCommand = new AsyncCommand<ReportLine>(UndoReportLineAsync);
        _s.ChangesApplied += () =>
        {
            if (IsActive)
            {
                _ = RefreshAfterChangeAsync();
            }
        };
        SelectRecommendedCommand = new RelayCommand(() =>
        {
            _userChoices.Clear();
            foreach (var t in Tweaks)
            {
                t.IsSelected = t.Plan.DefaultSelected;
            }
        });
    }

    public ICommand ScanCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand GoToStepCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand OpenLinkCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand OpenPanelCommand { get; }
    public ICommand SelectRecommendedCommand { get; }
    public ICommand UndoTweakCommand { get; }
    public ICommand UndoReportLineCommand { get; }

    public GameDefinition Target => _target;

    public string GameName => _target.Name;

    /// <summary>The wizard of the game currently shown. Only it rescans after changes; the other one catches up when shown.</summary>
    public bool IsActive { get; set; }

    public ObservableCollection<PassportRow> Passport { get; } = new();
    public ObservableCollection<Advice> Diagnosis { get; } = new();
    public ObservableCollection<TweakItemVm> Tweaks { get; } = new();
    public ObservableCollection<LaunchOptionVm> LaunchOptions { get; } = new();
    public ObservableCollection<PreviewLine> Preview { get; } = new();
    public ObservableCollection<ReportLine> Report { get; } = new();

    public int Step
    {
        get => _step;
        set
        {
            if (Set(ref _step, Math.Clamp(value, 0, 3)))
            {
                OnPropertyChanged(nameof(StepTitle));
            }
        }
    }

    public string StepTitle => Step switch
    {
        0 => "Шаг 1 из 4. Сканирование ПК",
        1 => "Шаг 2 из 4. Диагноз: что мешает (по важности)",
        2 => "Шаг 3 из 4. План оптимизации",
        _ => "Шаг 4 из 4. Применение, отчёт и откат",
    };

    public bool Busy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string ProfileText => _s.Snapshot is null ? string.Empty : ProfileClassifier.Describe(_s.Snapshot.Profile);

    public string ProfileShort => _s.Snapshot?.Profile switch
    {
        PcProfile.Weak => "Слабый ПК",
        PcProfile.Medium => "Средний ПК",
        PcProfile.Strong => "Мощный ПК",
        _ => "Не сканировано",
    };

    public string GpuShort => _s.Snapshot?.PrimaryGpu?.Name ?? "Нажмите «Сканировать»";

    public int CriticalCount => Diagnosis.Count(d => d.Severity == Severity.Critical);
    public int WarningCount => Diagnosis.Count(d => d.Severity == Severity.Warning);
    public int ImproveCount => Tweaks.Count(t => t.CanSelect && t.Plan.RecommendedHere && !t.IsDisputed);
    public int OptimalCount => Tweaks.Count(t => t.Plan.Status == TweakStatus.AlreadyOptimal);
    public bool Scanned => _s.Snapshot is not null;

    public string ScanButtonText => Scanned ? "Сканировать заново" : "Сканировать ПК";

    public string ScanAgeText => _s.Snapshot is { } s ? $"Сканирование от {s.ScannedAt:dd.MM.yyyy HH:mm}. Результат сохранён: при следующем запуске сканировать заново не нужно." : string.Empty;

    private void RaiseSummary()
    {
        foreach (var n in new[] { nameof(ProfileShort), nameof(GpuShort), nameof(CriticalCount), nameof(WarningCount), nameof(ImproveCount), nameof(OptimalCount), nameof(Scanned), nameof(ScanButtonText), nameof(ScanAgeText) })
        {
            OnPropertyChanged(n);
        }
    }

    public string PresetHint => _target.Id == GameId.Cs2
        ? $"Пресет по умолчанию подобран под профиль: {ProfileText}. Меняются только существующие ключи {_target.VideoFileName}; разрешение экрана не трогается. " +
          "Для соревновательной игры важнее ровный FPS: даже на мощном ПК по умолчанию выбран «Баланс»."
        : $"Пресет по умолчанию подобран под профиль: {ProfileText}. Меняются только существующие ключи {_target.VideoFileName}. " +
          "Разрешение экрана не трогается: на слабых ПК снижается рендер-масштаб, чтобы интерфейс оставался чётким.";

    public bool DryRun
    {
        get => _dryRun;
        set => Set(ref _dryRun, value);
    }

    public bool CreateRestorePoint
    {
        get => _createRestorePoint;
        set => Set(ref _createRestorePoint, value);
    }

    public bool IsMaxFps
    {
        get => _game.Preset == VideoPreset.MaxFps;
        set => SetPreset(value, VideoPreset.MaxFps);
    }

    public bool IsBalance
    {
        get => _game.Preset == VideoPreset.Balance;
        set => SetPreset(value, VideoPreset.Balance);
    }

    public bool IsQuality
    {
        get => _game.Preset == VideoPreset.Quality;
        set => SetPreset(value, VideoPreset.Quality);
    }

    public string FpsMaxText
    {
        get => _fpsMaxText;
        set
        {
            if (Set(ref _fpsMaxText, value))
            {
                _game.FpsMax = int.TryParse(value, out var v) && v is >= 30 and <= 1000 ? v : null;
                Replan();
            }
        }
    }

    public bool VariableRefresh
    {
        get => _game.VariableRefresh;
        set
        {
            _game.VariableRefresh = value;
            OnPropertyChanged();
            var s = _s.Snapshot;
            if (s is not null)
            {
                FpsMaxText = LaunchOptionsComposer.SuggestFpsMax(s.Display?.RefreshHz ?? 60, s.Profile == PcProfile.Weak || s.IsLaptop, value).ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public bool RemoveMyths
    {
        get => _game.RemoveMythLaunchOptions;
        set
        {
            _game.RemoveMythLaunchOptions = value;
            OnPropertyChanged();
            Replan();
        }
    }

    public string? MythsText
    {
        get => _mythsText;
        private set => Set(ref _mythsText, value);
    }

    public string ApiText
    {
        get => _apiText;
        private set => Set(ref _apiText, value);
    }

    public string HowToCompare => _target.HowToCompareApi;

    public string MythsLabel => _target.Id == GameId.Cs2
        ? "Убрать бесполезные параметры из CS:GO (-novid, -tickrate, +cl_updaterate и т. п.)"
        : "Убрать устаревшие и бесполезные параметры (-threads, -prewarm и т. п.)";

    public VendorGuide? Vendor
    {
        get => _vendor;
        private set => Set(ref _vendor, value);
    }

    private void SetPreset(bool on, VideoPreset preset)
    {
        if (!on || _game.Preset == preset)
        {
            return;
        }

        _game.Preset = preset;
        OnPropertyChanged(nameof(IsMaxFps));
        OnPropertyChanged(nameof(IsBalance));
        OnPropertyChanged(nameof(IsQuality));
        Replan();
    }

    private async Task ScanAsync()
    {
        Busy = true;
        Status = "Сканирую систему…";
        try
        {
            var snapshot = await Task.Run(_s.Scan);
            _s.Snapshot = snapshot;
            await PrepareAsync(snapshot);
            if (Step == 0)
            {
                Step = 1;
            }
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Called when this game's page is shown: uses a scan made on the other game's page, or replans after changes made there.
    /// </summary>
    public async Task ActivateAsync()
    {
        IsActive = true;
        if (_s.Snapshot is null && !_s.QuickScanTried && !Busy)
        {
            await QuickStartAsync();
            return;
        }

        var snapshot = _s.Snapshot;
        if (snapshot is null || ReferenceEquals(snapshot, _preparedFor) || Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            if (_preparedFor is null)
            {
                await PrepareAsync(snapshot);
            }
            else
            {
                _preparedFor = snapshot;
                Replan();
            }
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>First page shown after start: picks up the saved scan instead of asking for a new one.</summary>
    private async Task QuickStartAsync()
    {
        _s.QuickScanTried = true;
        Busy = true;
        Status = "Загружаю прошлое сканирование…";
        try
        {
            var snapshot = await Task.Run(_s.QuickScan);
            if (snapshot is null)
            {
                Status = "Нажмите «Сканировать», чтобы начать. Ничего не меняется без вашего подтверждения.";
                return;
            }

            _s.Snapshot = snapshot;
            await PrepareAsync(snapshot);
            Status = "Загружен прошлый скан: железо то же, Steam и игры проверены заново. Нажмите «Далее».";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Быстрый старт по сохранённому скану: {ex}");
            Status = "Нажмите «Сканировать», чтобы начать. Ничего не меняется без вашего подтверждения.";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Builds options, plan and diagnosis for this game from a snapshot.</summary>
    private async Task PrepareAsync(SystemSnapshot snapshot)
    {
        {
            _preparedFor = snapshot;
            var steam = snapshot.For(_target.Id);
            FillPassport(snapshot);

            string? currentLaunch = null;
            if (steam.LocalConfigPath is not null)
            {
                try
                {
                    currentLaunch = LocalConfigEditor.GetLaunchOptions(_s.Fs.ReadAllText(steam.LocalConfigPath), _target.AppId);
                }
                catch (Core.Vdf.VdfParseException ex)
                {
                    Log.Warn($"localconfig.vdf: {ex.Message}");
                }
            }

            _game = TweakCatalog.DefaultGameOptions(_target, snapshot, currentLaunch);
            _suppressReplan = true;
            LaunchOptions.Clear();
            foreach (var o in _target.Launch.All)
            {
                LaunchOptions.Add(new LaunchOptionVm(o, _game.LaunchTokens.Contains(o.Token), OnLaunchOptionChanged));
            }

            _fpsMaxText = (_game.FpsMax ?? 60).ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(FpsMaxText));
            OnPropertyChanged(nameof(IsMaxFps));
            OnPropertyChanged(nameof(IsBalance));
            OnPropertyChanged(nameof(IsQuality));
            OnPropertyChanged(nameof(VariableRefresh));
            OnPropertyChanged(nameof(RemoveMyths));
            OnPropertyChanged(nameof(ProfileText));
            OnPropertyChanged(nameof(PresetHint));
            _suppressReplan = false;

            var myths = LaunchOptionsComposer.Analyze(currentLaunch, _target.Launch).MythTokens;
            MythsText = myths.Count == 0
                ? null
                : "В текущих параметрах запуска найдены бесполезные/устаревшие: " + string.Join("; ", myths.Select(m => $"{m.Token} — {m.Reason}"));

            var api = ApiAdvisor.Recommend(_target, snapshot.PrimaryGpu, snapshot.Cpu);
            ApiText = api.Token.Length == 0
                ? $"Рекомендация для вашей системы: {api.Title}. {api.Reason}"
                : $"Рекомендация для вашей системы: {api.Title} ({api.Token}). {api.Reason}";
            Vendor = VendorAdvice.For(snapshot.PrimaryGpu, snapshot, _target);

            _userChoices.Clear();
            Tweaks.Clear();
            Replan();

            var health = await Task.Run(() => new HealthChecks(_s.Commands).Run(snapshot, _target));
            var background = await Task.Run(BackgroundApps.FindRunning);
            Diagnosis.Clear();
            foreach (var a in DiagnosisEngine.Diagnose(_target, snapshot, Tweaks.Select(t => t.Plan).ToList(), health, background, DateTime.Now))
            {
                Diagnosis.Add(a);
            }

            RaiseSummary();

            Status = snapshot.ScanWarnings.Count == 0
                ? $"Готово. Профиль: {ProfileClassifier.Describe(snapshot.Profile)}. Нажмите «Далее»."
                : $"Готово с предупреждениями ({snapshot.ScanWarnings.Count}). Профиль: {ProfileClassifier.Describe(snapshot.Profile)}.";
        }
    }

    private void OnLaunchOptionChanged()
    {
        if (_suppressReplan)
        {
            return;
        }

        // DX11 and Vulkan are mutually exclusive: picking one clears the other.
        var dx = LaunchOptions.FirstOrDefault(o => o.Token == _target.Launch.Dx11Token);
        var vk = LaunchOptions.FirstOrDefault(o => o.Token == _target.Launch.VulkanToken);
        if (dx is not null && vk is not null && dx.IsSelected && vk.IsSelected)
        {
            _suppressReplan = true;
            if (_game.LaunchTokens.Contains(dx.Token))
            {
                dx.IsSelected = false;
            }
            else
            {
                vk.IsSelected = false;
            }

            _suppressReplan = false;
        }

        _game.LaunchTokens = LaunchOptions.Where(o => o.IsSelected).Select(o => o.Token).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Replan();
    }

    private void FillPassport(SystemSnapshot s)
    {
        Passport.Clear();
        foreach (var g in s.Gpus)
        {
            var driver = g.DriverVersion.Length == 0
                ? "драйвер не определён"
                : $"драйвер {(g.DriverDisplayVersion is null ? g.DriverVersion : $"{g.DriverDisplayVersion} ({g.DriverVersion})")} от {g.DriverDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "?"}";
            Passport.Add(new PassportRow(g.IsIntegrated ? "Встроенная графика" : "Видеокарта", $"{g.Name} · {g.VramGb:0.#} ГБ · {driver}"));
        }

        if (s.IsHybridGraphics)
        {
            var runtime = s.For(_target.Id).RuntimeGpu;
            Passport.Add(new PassportRow("Гибридная графика", runtime is null ? "да (запустите игру и пересканируйте, чтобы увидеть активную карту)" : $"{_target.Name} сейчас рендерится на: {runtime}"));
        }

        if (s.Cpu is { } c)
        {
            Passport.Add(new PassportRow("Процессор", $"{c.Name} · {c.Cores} ядер / {c.LogicalProcessors} потоков"));
        }

        if (s.Memory is { } m)
        {
            var speed = m.Modules.Count > 0 && m.Modules[0].SpeedMhz > 0 ? $" · {m.Modules[0].SpeedMhz} МТ/с" : string.Empty;
            var channels = m.LikelyDualChannel switch
            {
                true => "вероятно, двухканальный режим",
                false => "вероятно, одноканальный режим",
                _ => "каналы неизвестны",
            };
            Passport.Add(new PassportRow("Память", $"{m.TotalGb:0.#} ГБ · модулей: {m.Modules.Count} ({channels}){speed}"));
        }

        if (s.Disks.Count > 0)
        {
            Passport.Add(new PassportRow("Диски", string.Join("; ", s.Disks.Select(d => $"{d.Model} ({DiskName(d.Kind)}, {d.SizeBytes / 1e9:0} ГБ)"))));
        }

        if (s.Display is { } d)
        {
            Passport.Add(new PassportRow("Монитор", d.RefreshHz > 0 ? $"{d.Width}×{d.Height} @ {d.RefreshHz} Гц" : $"{d.Width}×{d.Height}, частота не определена"));
        }

        if (s.Windows is { } w)
        {
            Passport.Add(new PassportRow("Windows", $"{w.ProductName} {w.DisplayVersion}".Trim() + $" (сборка {w.Build})"));
        }

        Passport.Add(new PassportRow("Питание", s.PowerPlan?.ActiveName ?? "?"));
        Passport.Add(new PassportRow("Тип ПК", s.IsLaptop ? "Ноутбук" : "Настольный"));
        var steam = s.For(_target.Id);
        Passport.Add(new PassportRow("Steam", steam.SteamPath ?? "не найден"));
        Passport.Add(new PassportRow(_target.Name, steam.GameInstallDir is null ? "не найдена" : $"{steam.GameInstallDir} ({DiskName(steam.DiskKind)}){(steam.GameRunning ? " — запущена" : string.Empty)}"));
        if (steam.VideoConfigPaths.Count > 0)
        {
            Passport.Add(new PassportRow(_target.VideoFileName, string.Join("; ", steam.VideoConfigPaths)));
        }

        if (steam.CloudCfgDirs.Count > 0)
        {
            Passport.Add(new PassportRow("Steam Cloud", $"настройки {_target.Name} синхронизируются с облаком — облако может вернуть старые значения после запуска игры"));
        }

        Passport.Add(new PassportRow("Профиль", ProfileClassifier.Describe(s.Profile)));
        foreach (var warn in s.ScanWarnings)
        {
            Passport.Add(new PassportRow("Предупреждение", warn));
        }
    }

    private static string DiskName(DiskKind k) => k switch
    {
        DiskKind.Nvme => "NVMe SSD",
        DiskKind.Ssd => "SSD",
        DiskKind.Hdd => "HDD",
        _ => "тип неизвестен",
    };

    private void Replan()
    {
        if (_suppressReplan || _s.Snapshot is null)
        {
            return;
        }

        foreach (var t in Tweaks)
        {
            if (t.CanSelect && t.IsSelected != t.Plan.DefaultSelected)
            {
                _userChoices[t.Id] = t.IsSelected;
            }
        }

        var ctx = new TweakContext { Snapshot = _s.Snapshot, Registry = _s.Registry, FileSystem = _s.Fs, Commands = _s.Commands, Game = _game };
        var plans = TweakCatalog.PlanAll(ctx, _target);
        Tweaks.Clear();
        foreach (var p in plans)
        {
            Tweaks.Add(new TweakItemVm(p, _userChoices.TryGetValue(p.Tweak.Id, out var chosen) ? chosen : p.DefaultSelected, _s.ActiveChangeFor(p.Tweak.Id)));
        }

        RaiseSummary();
    }

    private void OpenVendorPanel()
    {
        foreach (var candidate in Vendor?.PanelCandidates ?? Array.Empty<string>())
        {
            try
            {
                if (candidate.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", candidate) { UseShellExecute = true })?.Dispose();
                    return;
                }

                if (File.Exists(candidate))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(candidate) { UseShellExecute = true })?.Dispose();
                    return;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Warn($"Не удалось открыть {candidate}: {ex.Message}");
            }
        }

        Ui.Info($"Не удалось открыть панель автоматически. {Vendor?.HowToOpen}");
    }

    private async Task ApplyAsync()
    {
        var snapshot = _s.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        Busy = true;
        Preview.Clear();
        Report.Clear();
        try
        {
            // Game/Steam may have been started or closed since the scan.
            var fresh = await Task.Run(() => new SteamLocator(_s.Fs, _s.Registry).Locate(_target, SystemScanner.IsProcessRunning));
            var old = snapshot.For(_target.Id);
            var games = snapshot.Games.ToDictionary(g => g.Key, g => g.Value);
            games[_target.Id] = fresh with { DiskKind = old.DiskKind, RuntimeGpu = old.RuntimeGpu };
            _s.Snapshot = snapshot = snapshot with { Games = games };
            _preparedFor = snapshot;
            Replan();

            var selected = Tweaks.Where(t => t.IsSelected).Select(t => t.Plan).ToList();
            if (selected.Count == 0)
            {
                Say("Нет выбранных изменений, которые можно применить прямо сейчас (проверьте статусы в плане).");
                return;
            }

            foreach (var p in selected)
            {
                foreach (var line in _s.Executor.Preview(p.Actions))
                {
                    Preview.Add(line);
                }
            }

            if (DryRun)
            {
                Say($"Режим dry-run: показано {Preview.Count} изменений, ничего не изменено.");
                Say("Снимите галочку «Только показать (dry-run)» и нажмите «Применить» ещё раз, чтобы применить.");
                Status = "Dry-run выполнен — ничего не изменено.";
                return;
            }

            var restart = selected.Any(p => p.Tweak.RequiresRestart) ? "\n\nЧасть изменений вступит в силу после перезагрузки." : string.Empty;
            if (!Ui.Confirm($"Применить выбранное? Пунктов: {selected.Count}, шагов: {Preview.Count}.\n\nПеред каждым шагом сохраняется копия, всё можно откатить на вкладке «Откат».{restart}"))
            {
                return;
            }

            var admin = selected.Where(p => p.RequiresAdmin).ToList();
            var user = selected.Where(p => !p.RequiresAdmin).ToList();

            if (admin.Count > 0 || CreateRestorePoint)
            {
                var request = new ElevatedRequest
                {
                    Mode = "apply",
                    CreateRestorePoint = CreateRestorePoint,
                    Items = admin.Select(p => new ElevatedItem { TweakId = p.Tweak.Id, Title = p.Tweak.Title, Actions = p.Actions.ToList() }).ToList(),
                };
                Say(admin.Count > 0 ? "Запрашиваю права администратора (точка восстановления и системные настройки)…" : "Запрашиваю права администратора для точки восстановления…");
                var result = await Task.Run(() => AppServices.RunElevated(request));
                if (result is null)
                {
                    Say("Запрос прав администратора отклонён.");
                    if (admin.Count > 0)
                    {
                        Say("Пропущено (нужны права администратора): " + string.Join(", ", admin.Select(a => a.Tweak.Title)));
                    }

                    if (user.Count > 0 && CreateRestorePoint &&
                        !Ui.Confirm("Точку восстановления создать не удалось (нужны права администратора).\n\nПродолжить без неё? Собственные копии файлов и реестра всё равно будут сохранены."))
                    {
                        Say("Отменено пользователем.");
                        return;
                    }
                }
                else
                {
                    if (result.RestorePointMessage is not null)
                    {
                        Say((result.RestorePointCreated == true ? "✓ " : "⚠ ") + "Точка восстановления: " + result.RestorePointMessage);
                    }

                    foreach (var r in result.Items)
                    {
                        var title = admin.FirstOrDefault(a => a.Tweak.Id == r.TweakId)?.Tweak.Title ?? r.TweakId;
                        Say(r.Success ? $"✓ {title}" : $"✗ {title}: {r.Error} (изменения этого пункта откачены)", r.Success ? r.ChangeSetId : null);
                        _s.History.Add(new HistoryEntry(DateTime.Now, r.Success ? "Применено" : "Ошибка", title, r.Error ?? string.Empty));
                    }
                }
            }

            foreach (var p in user)
            {
                var r = await Task.Run(() => _s.Executor.Apply(p.Tweak.Id, p.Tweak.Title, p.Actions, dryRun: false));
                Say(r.Success ? $"✓ {p.Tweak.Title}" : $"✗ {p.Tweak.Title}: {r.Error} (изменения этого пункта откачены)", r.Success ? r.ChangeSetId : null);
                _s.History.Add(new HistoryEntry(DateTime.Now, r.Success ? "Применено" : "Ошибка", p.Tweak.Title,
                    string.Join("; ", p.Actions.Select(a => a.Description))));
            }

            if (selected.Any(p => p.Tweak.RequiresRestart))
            {
                Say("Перезагрузите компьютер, чтобы изменения HAGS вступили в силу.");
            }

            Say("Готово. Любой пункт можно отменить кнопкой «Отменить» рядом с ним, в плане (шаг 3) или на вкладке «Откат». Чтобы проверить эффект, запишите матч на вкладке «Монитор матча».");
            Status = "Изменения применены.";
        }
        finally
        {
            Busy = false;
        }

        _s.NotifyChangesApplied();
    }

    private void Say(string text, string? changeSetId = null) => Report.Add(new ReportLine(text, changeSetId));

    private async Task UndoTweakAsync(TweakItemVm? item)
    {
        if (item?.Applied is { } set && await _s.UndoAsync(set.Id) is { } messages)
        {
            Status = $"Отменено: {item.Title}. " + string.Join(" ", messages);
        }
    }

    private async Task UndoReportLineAsync(ReportLine? line)
    {
        if (line?.ChangeSetId is { } id && await _s.UndoAsync(id) is not null)
        {
            Status = "Изменение отменено.";
        }
    }

    /// <summary>After any apply/undo (here or on other tabs): rescan, so statuses and undo buttons reflect reality.</summary>
    private async Task RefreshAfterChangeAsync()
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

        if (_s.Snapshot is null || Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            _s.Snapshot = _preparedFor = await Task.Run(_s.Scan);
            Replan();
        }
        finally
        {
            Busy = false;
        }
    }

    public string ReportText()
    {
        var sb = new StringBuilder();
        foreach (var line in Report)
        {
            sb.AppendLine(line.Text);
        }

        return sb.ToString();
    }
}
