using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Crash;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.App.ViewModels;

public sealed record FixButton(QuickFix Fix, string Title, bool Reversible);

public sealed record IssueStep(int Number, string Text);

public sealed class IssueVm
{
    public IssueVm(KnownIssue issue)
    {
        Issue = issue;
        Steps = issue.Steps.Select((s, i) => new IssueStep(i + 1, s)).ToList();
        Fixes = issue.Fixes.Select(f => new FixButton(f, KnownIssuesViewModel.FixTitle(f), KnownIssuesViewModel.IsReversible(f))).ToList();
        (IconKey, Tone) = issue.Id switch
        {
            "late-game-fps" => ("IconPulse", "Accent"),
            "stutter-after-update" => ("IconBolt", "Warn"),
            "microstutter" => ("IconClock", "Warn"),
            "black-screen" => ("IconAlertCircle", "Crit"),
            "wrong-resolution" => ("IconMonitor", "Info"),
            "crash-in-game" => ("IconShield", "Crit"),
            "lag-ping" => ("IconWifi", "Info"),
            "update-stuck" => ("IconDisk", "Info"),
            "cs2-low-fps" => ("IconPulse", "Accent"),
            "cs2-stutter-after-update" => ("IconBolt", "Warn"),
            "cs2-input-lag" => ("IconClock", "Warn"),
            "cs2-microstutter" => ("IconClock", "Warn"),
            "cs2-black-screen" => ("IconAlertCircle", "Crit"),
            "cs2-resolution" => ("IconMonitor", "Info"),
            "cs2-crash" => ("IconShield", "Crit"),
            "cs2-vac-verify" => ("IconShieldCheck", "Crit"),
            "cs2-obs-black" => ("IconMonitor", "Info"),
            "cs2-lag" => ("IconWifi", "Info"),
            "cs2-update-stuck" => ("IconDisk", "Info"),
            _ => ("IconAlert", "Accent"),
        };
    }

    public KnownIssue Issue { get; }
    public string Symptom => Issue.Symptom;
    public string Cause => Issue.Cause;
    public string Evidence => "Источник и уверенность: " + Issue.Evidence;
    public IReadOnlyList<IssueStep> Steps { get; }
    public IReadOnlyList<FixButton> Fixes { get; }
    public bool HasFixes => Fixes.Count > 0;

    public string FixCountText => Fixes.Count switch
    {
        0 => Steps.Count switch { 1 => "1 шаг", < 5 => $"{Steps.Count} шага", _ => $"{Steps.Count} шагов" },
        1 => "1 исправление",
        < 5 => $"{Fixes.Count} исправления",
        _ => $"{Fixes.Count} исправлений",
    };

    /// <summary>Resource keys of the card's icon and colour ("Accent", "Warn", ...).</summary>
    public string IconKey { get; }
    public string Tone { get; }
    public object? Icon => System.Windows.Application.Current?.TryFindResource(IconKey);
    public object? ToneBrush => System.Windows.Application.Current?.TryFindResource(Tone);
    public object? ToneSoft => System.Windows.Application.Current?.TryFindResource(Tone + "Soft");

    public bool Matches(string query) =>
        Symptom.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Cause.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Steps.Any(s => s.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
}

public sealed class AppliedFixVm
{
    public AppliedFixVm(ChangeSet set)
    {
        Set = set;
    }

    public ChangeSet Set { get; }
    public string Text => $"{Set.Title} · {Set.CreatedUtc.ToLocalTime():dd.MM HH:mm}";
}

/// <summary>"Проблемы": symptom → cause → steps → one-click fixes (reversible ones are journaled and undoable in place).</summary>
public sealed class KnownIssuesViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly GameDefinition _game;
    private readonly Action<int> _navigate;
    private string? _status;
    private string _searchText = string.Empty;

    public KnownIssuesViewModel(AppServices services, GameDefinition game, Action<int> navigate)
    {
        _s = services;
        _game = game;
        _navigate = navigate;
        Issues = game.Issues.Select(i => new IssueVm(i)).ToList();
        VisibleIssues = new ObservableCollection<IssueVm>(Issues);
        RunFixCommand = new AsyncCommand<FixButton>(RunFixAsync);
        UndoFixCommand = new AsyncCommand<AppliedFixVm>(async vm =>
        {
            if (vm is not null && await _s.UndoAsync(vm.Set.Id) is { } m)
            {
                Status = "Исправление отменено. " + string.Join(" ", m);
            }
        });
        DiskUsageCommand = new AsyncCommand(LoadDiskUsageAsync);
        OpenFolderCommand = new RelayCommand(p =>
        {
            if (p is string path)
            {
                Ui.OpenFolder(path);
            }
        });
        _s.ChangesApplied += () => System.Windows.Application.Current.Dispatcher.Invoke(ReloadApplied);
        ReloadApplied();
    }

    public IReadOnlyList<IssueVm> Issues { get; }
    public ObservableCollection<IssueVm> VisibleIssues { get; }
    public bool NothingFound => VisibleIssues.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                var q = _searchText.Trim();
                VisibleIssues.Clear();
                foreach (var i in Issues.Where(i => q.Length == 0 || i.Matches(q)))
                {
                    VisibleIssues.Add(i);
                }

                OnPropertyChanged(nameof(NothingFound));
            }
        }
    }
    public ObservableCollection<AppliedFixVm> AppliedFixes { get; } = new();
    public ObservableCollection<DiskUsageItem> DiskUsage { get; } = new();
    public ICommand RunFixCommand { get; }
    public ICommand UndoFixCommand { get; }
    public ICommand DiskUsageCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public string? Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public static string FixTitle(QuickFix f) => f switch
    {
        QuickFix.ResetVideoSettings => "Сбросить настройки графики",
        QuickFix.DisableAutoexec => "Отключить autoexec.cfg",
        QuickFix.SwitchApi => "Переключить DX11 ↔ Vulkan",
        QuickFix.ClearShaderCache => "Очистить кеш шейдеров",
        QuickFix.VerifyFiles => "Проверить файлы игры",
        QuickFix.OpenNetworkTab => "Открыть тест сети",
        QuickFix.StartMatchMonitor => "Открыть монитор матча",
        _ => "Очистить кеш загрузки Steam",
    };

    public static bool IsReversible(QuickFix f) => f is QuickFix.ResetVideoSettings or QuickFix.DisableAutoexec or QuickFix.SwitchApi;

    private void ReloadApplied()
    {
        AppliedFixes.Clear();
        foreach (var set in _s.Store.Active().Where(c => c.TweakId.StartsWith(_game.FixPrefix, StringComparison.Ordinal)).OrderByDescending(c => c.CreatedUtc))
        {
            AppliedFixes.Add(new AppliedFixVm(set));
        }
    }

    public string Title => $"Проблемы {_game.ShortName}";

    public string GameName => _game.Name;

    private SteamInfo FreshSteam() => new SteamLocator(_s.Fs, _s.Registry).Locate(_game, SystemScanner.IsProcessRunning);

    private async Task RunFixAsync(FixButton? b)
    {
        if (b is null)
        {
            return;
        }

        switch (b.Fix)
        {
            case QuickFix.OpenNetworkTab:
                _navigate(MainViewModel.NetworkTab);
                return;
            case QuickFix.StartMatchMonitor:
                _navigate(MainViewModel.MonitorTab);
                return;
            case QuickFix.VerifyFiles:
                if (Ui.Confirm($"Запустить проверку целостности файлов {_game.Name} в Steam? Steam докачает повреждённые файлы."))
                {
                    CrashTools.StartValidate(_game);
                    _s.History.Add(new HistoryEntry(DateTime.Now, "Обслуживание", $"Проверка целостности файлов {_game.ShortName}", $"steam://validate/{_game.AppId}"));
                }

                return;
            case QuickFix.FlushSteamDownloadCache:
                if (Ui.Confirm("Очистить кеш загрузки Steam? Steam перезапустится и может попросить войти заново."))
                {
                    CrashTools.FlushSteamDownloadCache();
                    _s.History.Add(new HistoryEntry(DateTime.Now, "Обслуживание", "Очистка кеша загрузки Steam", "steam://flushconfig"));
                }

                return;
            case QuickFix.ClearShaderCache:
                await ClearShaderCacheAsync();
                return;
        }

        var steam = await Task.Run(FreshSteam);
        if (!steam.GameFound)
        {
            Ui.Info($"{_game.Name} не найдена.");
            return;
        }

        if (steam.GameRunning)
        {
            Ui.Info($"Закройте {_game.Name} — иначе при выходе она перезапишет файлы настроек.");
            return;
        }

        IReadOnlyList<PlannedAction> actions;
        string id, title, question;
        switch (b.Fix)
        {
            case QuickFix.ResetVideoSettings:
                actions = QuickFixes.PlanResetVideo(_game, steam);
                id = QuickFixes.ResetVideoId(_game);
                title = $"Сброс настроек графики {_game.ShortName}";
                question = $"Удалить {_game.VideoFileName} (включая копию Steam Cloud), чтобы игра создала настройки графики заново?\n\nКопии сохраняются — сброс можно отменить кнопкой «Отменить».";
                if (actions.Count == 0)
                {
                    Ui.Info($"{_game.VideoFileName} не найден — игра и так создаст его при запуске.");
                    return;
                }

                break;
            case QuickFix.DisableAutoexec:
                actions = QuickFixes.PlanDisableAutoexec(_s.Fs, steam);
                id = QuickFixes.DisableAutoexecId(_game);
                title = $"autoexec.cfg {_game.ShortName} временно отключён";
                question = "Переименовать autoexec.cfg в autoexec.cfg.disabled? Если проблема исчезнет — виновата одна из команд в нём.\n\nМожно отменить кнопкой «Отменить».";
                if (actions.Count == 0)
                {
                    Ui.Info("autoexec.cfg не найден — его нет, отключать нечего.");
                    return;
                }

                break;
            default:
                var (apiActions, line, blocker) = QuickFixes.PlanSwitchApi(_game, _s.Fs, steam);
                if (blocker is not null)
                {
                    System.Windows.Clipboard.SetText(line);
                    Ui.Info($"{blocker}\n\nНовая строка параметров запуска скопирована в буфер обмена:\n{line}");
                    return;
                }

                actions = apiActions;
                id = QuickFixes.SwitchApiId(_game);
                title = $"API {_game.ShortName} переключён ({(line.Contains("-vulkan", StringComparison.Ordinal) ? "Vulkan" : "DX11")})";
                question = $"Новая строка параметров запуска:\n{line}\n\nПрименить? Можно отменить кнопкой «Отменить».";
                break;
        }

        if (!Ui.Confirm(question))
        {
            return;
        }

        var r = await Task.Run(() => _s.Executor.Apply(id, title, actions, dryRun: false));
        _s.History.Add(new HistoryEntry(DateTime.Now, r.Success ? "Применено" : "Ошибка", title, string.Join("; ", actions.Select(a => a.Description))));
        Status = r.Success ? $"✓ {title}. Отменить можно кнопкой ниже." : $"✗ {title}: {r.Error}";
        _s.NotifyChangesApplied();
    }

    private async Task ClearShaderCacheAsync()
    {
        if (SystemScanner.IsProcessRunning(_game.ProcessName))
        {
            Ui.Info($"Закройте {_game.Name} перед очисткой кеша шейдеров.");
            return;
        }

        var steam = await Task.Run(FreshSteam);
        var vendors = _s.Snapshot?.RealGpus.Select(g => g.Vendor).ToList() ?? new List<GpuVendor> { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel };
        var caches = (await Task.Run(() => CrashTools.ShaderCaches(_game, steam, vendors))).Where(c => c.Exists).ToList();
        if (caches.Count == 0)
        {
            Ui.Info("Кеши шейдеров не найдены.");
            return;
        }

        if (!Ui.Confirm($"Очистить кеш шейдеров ({Ui.Size(caches.Sum(c => c.SizeBytes))})?\n\n{string.Join("\n", caches.Select(c => "• " + c.Name))}\n\n" +
                        "Это не откатывается, но и не нужно: кеш пересоздастся сам. Первые 1–2 матча могут подтормаживать."))
        {
            return;
        }

        var r = await Task.Run(() => CrashTools.Clean(caches));
        _s.History.Add(new HistoryEntry(DateTime.Now, "Обслуживание", "Очистка кеша шейдеров", $"Удалено {r.Deleted}, освобождено {Ui.Size(r.FreedBytes)}"));
        Status = $"Кеш шейдеров очищен: освобождено {Ui.Size(r.FreedBytes)}, занятых файлов пропущено: {r.Skipped}.";
    }

    private async Task LoadDiskUsageAsync()
    {
        var steam = await Task.Run(FreshSteam);
        var items = await Task.Run(() => QuickFixes.DiskUsage(_game, steam, CrashTools.DirSize));
        DiskUsage.Clear();
        foreach (var i in items)
        {
            DiskUsage.Add(i);
        }

        if (items.Count == 0)
        {
            Status = $"{_game.Name} не найдена или папок повторов и Мастерской нет.";
        }
    }
}
