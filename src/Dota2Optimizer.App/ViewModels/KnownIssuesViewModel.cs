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

public sealed class IssueVm
{
    public IssueVm(KnownIssue issue)
    {
        Issue = issue;
        Steps = issue.Steps.Select((s, i) => $"{i + 1}. {s}").ToList();
        Fixes = issue.Fixes.Select(f => new FixButton(f, KnownIssuesViewModel.FixTitle(f), KnownIssuesViewModel.IsReversible(f))).ToList();
    }

    public KnownIssue Issue { get; }
    public string Symptom => Issue.Symptom;
    public string Cause => Issue.Cause;
    public string Evidence => "Источник/уверенность: " + Issue.Evidence;
    public IReadOnlyList<string> Steps { get; }
    public IReadOnlyList<FixButton> Fixes { get; }
    public bool HasFixes => Fixes.Count > 0;
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

/// <summary>"Проблемы Dota 2": symptom → cause → steps → one-click fixes (reversible ones are journaled and undoable in place).</summary>
public sealed class KnownIssuesViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly Action<int> _navigate;
    private string? _status;

    public KnownIssuesViewModel(AppServices services, Action<int> navigate)
    {
        _s = services;
        _navigate = navigate;
        Issues = KnownIssues.All.Select(i => new IssueVm(i)).ToList();
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
        foreach (var set in _s.Store.Active().Where(c => c.TweakId.StartsWith("fix.", StringComparison.Ordinal)).OrderByDescending(c => c.CreatedUtc))
        {
            AppliedFixes.Add(new AppliedFixVm(set));
        }
    }

    private SteamInfo FreshSteam() => new SteamLocator(_s.Fs, _s.Registry).Locate(SystemScanner.IsProcessRunning);

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
                if (Ui.Confirm("Запустить проверку целостности файлов Dota 2 в Steam? Steam докачает повреждённые файлы."))
                {
                    CrashTools.StartValidate();
                    _s.History.Add(new HistoryEntry(DateTime.Now, "Обслуживание", "Проверка целостности файлов", "steam://validate/570"));
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
        if (!steam.DotaFound)
        {
            Ui.Info("Dota 2 не найдена.");
            return;
        }

        if (steam.DotaRunning)
        {
            Ui.Info("Закройте Dota 2 — иначе при выходе она перезапишет файлы настроек.");
            return;
        }

        IReadOnlyList<PlannedAction> actions;
        string id, title, question;
        switch (b.Fix)
        {
            case QuickFix.ResetVideoSettings:
                actions = DotaQuickFixes.PlanResetVideo(steam);
                id = DotaQuickFixes.ResetVideoId;
                title = "Сброс настроек графики Dota 2";
                question = "Удалить video.txt (включая копию Steam Cloud), чтобы игра создала настройки графики заново?\n\nКопии сохраняются — сброс можно отменить кнопкой «Отменить».";
                if (actions.Count == 0)
                {
                    Ui.Info("video.txt не найден — игра и так создаст его при запуске.");
                    return;
                }

                break;
            case QuickFix.DisableAutoexec:
                actions = DotaQuickFixes.PlanDisableAutoexec(_s.Fs, steam);
                id = DotaQuickFixes.DisableAutoexecId;
                title = "autoexec.cfg временно отключён";
                question = "Переименовать autoexec.cfg в autoexec.cfg.disabled? Если проблема исчезнет — виновата одна из команд в нём.\n\nМожно отменить кнопкой «Отменить».";
                if (actions.Count == 0)
                {
                    Ui.Info("autoexec.cfg не найден — его нет, отключать нечего.");
                    return;
                }

                break;
            default:
                var (apiActions, line, blocker) = DotaQuickFixes.PlanSwitchApi(_s.Fs, steam);
                if (blocker is not null)
                {
                    System.Windows.Clipboard.SetText(line);
                    Ui.Info($"{blocker}\n\nНовая строка параметров запуска скопирована в буфер обмена:\n{line}");
                    return;
                }

                actions = apiActions;
                id = DotaQuickFixes.SwitchApiId;
                title = $"API переключён ({(line.Contains("-vulkan", StringComparison.Ordinal) ? "Vulkan" : "DX11")})";
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
        if (SystemScanner.IsProcessRunning("dota2"))
        {
            Ui.Info("Закройте Dota 2 перед очисткой кеша шейдеров.");
            return;
        }

        var steam = await Task.Run(FreshSteam);
        var vendors = _s.Snapshot?.RealGpus.Select(g => g.Vendor).ToList() ?? new List<GpuVendor> { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel };
        var caches = (await Task.Run(() => CrashTools.ShaderCaches(steam, vendors))).Where(c => c.Exists).ToList();
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
        var items = await Task.Run(() => DotaQuickFixes.DiskUsage(steam, CrashTools.DirSize));
        DiskUsage.Clear();
        foreach (var i in items)
        {
            DiskUsage.Add(i);
        }

        if (items.Count == 0)
        {
            Status = "Dota 2 не найдена или папок повторов/кастомок нет.";
        }
    }
}
