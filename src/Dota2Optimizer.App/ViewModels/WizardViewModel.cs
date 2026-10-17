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
    public bool IsDisputed => Plan.Tweak.Disputed;
    public string StatusText => Plan.StatusText;
    public string Risk => "Риск: " + Plan.Tweak.Risk;
    public string? ManualSteps => Plan.Tweak.ManualSteps;
    public string? CopyText => Plan.CopyText;
    public bool RequiresAdmin => Plan.RequiresAdmin;
    public bool RequiresRestart => Plan.Tweak.RequiresRestart;
    public bool CanSelect => Plan.Status == TweakStatus.CanApply;

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
    private readonly Dictionary<string, bool> _userChoices = new();
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

    public WizardViewModel(AppServices services)
    {
        _s = services;
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
        _s.ChangesApplied += () => _ = RefreshAfterChangeAsync();
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

    public string PresetHint =>
        $"Пресет по умолчанию подобран под профиль: {ProfileText}. Меняются только существующие ключи video.txt. " +
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

    public string HowToCompare => ApiAdvisor.HowToCompare;

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
            FillPassport(snapshot);

            string? currentLaunch = null;
            if (snapshot.Steam.LocalConfigPath is not null)
            {
                try
                {
                    currentLaunch = LocalConfigEditor.GetLaunchOptions(_s.Fs.ReadAllText(snapshot.Steam.LocalConfigPath));
                }
                catch (Core.Vdf.VdfParseException ex)
                {
                    Log.Warn($"localconfig.vdf: {ex.Message}");
                }
            }

            _game = TweakCatalog.DefaultGameOptions(snapshot, currentLaunch);
            _suppressReplan = true;
            LaunchOptions.Clear();
            foreach (var o in LaunchOptionCatalog.All)
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

            var myths = LaunchOptionsComposer.Analyze(currentLaunch).MythTokens;
            MythsText = myths.Count == 0
                ? null
                : "В текущих параметрах запуска найдены бесполезные/устаревшие: " + string.Join("; ", myths.Select(m => $"{m.Token} — {m.Reason}"));

            var api = ApiAdvisor.Recommend(snapshot.PrimaryGpu, snapshot.Cpu);
            ApiText = $"Рекомендация для вашей системы: {api.Title} ({api.Token}). {api.Reason}";
            Vendor = VendorAdvice.For(snapshot.PrimaryGpu, snapshot);

            _userChoices.Clear();
            Tweaks.Clear();
            Replan();

            var health = await Task.Run(() => new HealthChecks(_s.Commands).Run(snapshot));
            var background = await Task.Run(BackgroundApps.FindRunning);
            Diagnosis.Clear();
            foreach (var a in DiagnosisEngine.Diagnose(snapshot, Tweaks.Select(t => t.Plan).ToList(), health, background, DateTime.Now))
            {
                Diagnosis.Add(a);
            }

            Status = snapshot.ScanWarnings.Count == 0
                ? $"Готово. Профиль: {ProfileClassifier.Describe(snapshot.Profile)}. Нажмите «Далее»."
                : $"Готово с предупреждениями ({snapshot.ScanWarnings.Count}). Профиль: {ProfileClassifier.Describe(snapshot.Profile)}.";
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

    private void OnLaunchOptionChanged()
    {
        if (_suppressReplan)
        {
            return;
        }

        // DX11 and Vulkan are mutually exclusive: picking one clears the other.
        var dx = LaunchOptions.FirstOrDefault(o => o.Token == "-dx11");
        var vk = LaunchOptions.FirstOrDefault(o => o.Token == "-vulkan");
        if (dx is not null && vk is not null && dx.IsSelected && vk.IsSelected)
        {
            _suppressReplan = true;
            if (_game.LaunchTokens.Contains("-dx11"))
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
            Passport.Add(new PassportRow("Гибридная графика", s.DotaRuntimeGpu is null ? "да (запустите игру и пересканируйте, чтобы увидеть активную карту)" : $"Dota 2 сейчас рендерится на: {s.DotaRuntimeGpu}"));
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
        Passport.Add(new PassportRow("Steam", s.Steam.SteamPath ?? "не найден"));
        Passport.Add(new PassportRow("Dota 2", s.Steam.DotaInstallDir is null ? "не найдена" : $"{s.Steam.DotaInstallDir} ({DiskName(s.DotaDiskKind)}){(s.Steam.DotaRunning ? " — запущена" : string.Empty)}"));
        if (s.Steam.VideoConfigPaths.Count > 0)
        {
            Passport.Add(new PassportRow("video.txt", string.Join("; ", s.Steam.VideoConfigPaths)));
        }

        if (s.Steam.CloudCfgDirs.Count > 0)
        {
            Passport.Add(new PassportRow("Steam Cloud", "настройки Dota 2 синхронизируются с облаком — облако может вернуть старые значения после запуска игры"));
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
        var plans = TweakCatalog.PlanAll(ctx);
        Tweaks.Clear();
        foreach (var p in plans)
        {
            Tweaks.Add(new TweakItemVm(p, _userChoices.TryGetValue(p.Tweak.Id, out var chosen) ? chosen : p.DefaultSelected, _s.ActiveChangeFor(p.Tweak.Id)));
        }
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
            var steam = await Task.Run(() => new SteamLocator(_s.Fs, _s.Registry).Locate(SystemScanner.IsProcessRunning));
            _s.Snapshot = snapshot = snapshot with { Steam = steam };
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

            Say("Готово. Любой пункт можно отменить кнопкой «Отменить» рядом с ним, в плане (шаг 3) или на вкладке «Откат». Сделайте «Замер FPS» после, чтобы сравнить с «до».");
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
            _s.Snapshot = await Task.Run(_s.Scan);
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
