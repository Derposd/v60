using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.GtaRp;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

public sealed class SpecGameVm : ObservableObject
{
    private bool _selected;

    public SpecGameVm(SpecGame game, Action<SpecGameVm> select)
    {
        Game = game;
        Title = SpecCatalog.TitleOf(game);
        SelectCommand = new RelayCommand(() => select(this));
    }

    public SpecGame Game { get; }
    public string Title { get; }
    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

/// <summary>What the checkbox of a row shows.</summary>
public enum SpecMark
{
    /// <summary>Can be marked; empty box.</summary>
    Off,

    /// <summary>Marked for «Применить изменения»; accent fill.</summary>
    Marked,

    /// <summary>Already in effect (by this app or by Windows); ink fill, not clickable.</summary>
    InEffect,

    /// <summary>Not applicable or manual only; muted box, not clickable.</summary>
    Unavailable,
}

public sealed class SpecRowVm : ObservableObject
{
    private readonly Action<SpecRowVm> _changed;
    private TweakToggle? _state;
    private bool _marked;
    private string? _error;
    private string _number = "··";

    public SpecRowVm(SpecItem item, Action<SpecRowVm> changed)
    {
        Item = item;
        _changed = changed;
        ToggleCommand = new RelayCommand(() => IsMarked = !IsMarked, () => CanMark);
    }

    public SpecItem Item { get; }

    /// <summary>Position among the rows shown («01»…); rows hidden as unsuitable are not counted.</summary>
    public string Number
    {
        get => _number;
        set => Set(ref _number, value);
    }

    /// <summary>Doesn't apply to this PC: hidden unless «Показать неподходящие» is on.</summary>
    public bool IsUnsuitable => _state?.Plan.Status == TweakStatus.NotApplicable;
    public string Name => Item.Name;
    public string Description => Item.Description;
    public SpecRisk Risk => Item.Risk;

    public string RiskText => Risk switch
    {
        SpecRisk.Safe => "безопасно",
        SpecRisk.Mid => "могут быть проблемы",
        _ => "осторожно",
    };

    public ICommand ToggleCommand { get; }
    public TweakToggle? State => _state;
    public bool CanMark => _state?.CanTurnOn == true;

    public bool IsMarked
    {
        get => _marked;
        set
        {
            if (value && !CanMark)
            {
                return;
            }

            if (Set(ref _marked, value))
            {
                Notify();
                _changed(this);
            }
        }
    }

    public SpecMark Mark => _marked ? SpecMark.Marked
        : _state is null ? SpecMark.Off
        : _state.IsOn ? SpecMark.InEffect
        : _state.CanTurnOn ? SpecMark.Off
        : SpecMark.Unavailable;

    /// <summary>Honest labels in one mono line: the effect, «спорно», restart — then the row's current state.</summary>
    public string StatusLine
    {
        get
        {
            var parts = new List<string> { "эффект: " + Item.Tweak.Effectiveness.Ru().ToLowerInvariant() };
            if (Item.Tweak.Disputed)
            {
                parts.Add("спорно");
            }

            if (Item.Tweak.RequiresRestart)
            {
                parts.Add("нужна перезагрузка");
            }

            parts.Add(_state switch
            {
                null => "проверяю…",
                { CanTurnOff: true } => "включено программой, откат — «Вернуть всё как было»",
                { IsOn: true } => "уже так в Windows, программа ничего не меняла",
                { Plan.Status: TweakStatus.ManualOnly or TweakStatus.NotApplicable or TweakStatus.Blocked } => "недоступно: " + _state.Plan.StatusText.TrimEnd('.'),
                _ => string.Empty,
            });
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }
    }

    public bool HasStatus => !HasError;

    /// <summary>Why the last apply of this row failed; shown in red on the row until the next check.</summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (Set(ref _error, value))
            {
                Notify();
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public string ToolTipText => _state?.Plan.StatusText is { Length: > 0 } t ? $"{Item.Tweak.Explanation}\n\nСейчас: {t}" : Item.Tweak.Explanation;

    public void Update(TweakToggle state)
    {
        _state = state;
        if (_marked && !state.CanTurnOn)
        {
            _marked = false;
            OnPropertyChanged(nameof(IsMarked));
        }

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(CanMark));
        OnPropertyChanged(nameof(IsUnsuitable));
        OnPropertyChanged(nameof(ToolTipText));
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Mark));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(HasError));
    }
}

/// <summary>A group heading in the table («Система», «Приватность и реклама», the game…).</summary>
public sealed record SpecGroupVm(string Title)
{
    public string Caps => Title.ToUpperInvariant();
}

/// <summary>A line of a dialog: a mono mark («01», «✓», «✗»), the text and an optional mono detail.</summary>
public sealed record SpecDialogLine(string Mark, string Text, string? Detail = null, bool Bad = false)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>An in-window dialog in the same paper style. Completes with true for the primary button.</summary>
public sealed class SpecDialogVm
{
    private readonly TaskCompletionSource<bool> _done = new();

    public SpecDialogVm(string kicker, string title, string body, IReadOnlyList<SpecDialogLine> lines, string primary, string? secondary, bool danger = false)
    {
        Kicker = kicker;
        Title = title;
        Body = body;
        Lines = lines;
        Primary = primary;
        Secondary = secondary;
        IsDanger = danger;
        PrimaryCommand = new RelayCommand(() => _done.TrySetResult(true));
        SecondaryCommand = new RelayCommand(() => _done.TrySetResult(false));
    }

    public string Kicker { get; }
    public string Title { get; }
    public string Body { get; }
    public bool HasBody => Body.Length > 0;
    public IReadOnlyList<SpecDialogLine> Lines { get; }
    public bool HasLines => Lines.Count > 0;
    public string Primary { get; }
    public string? Secondary { get; }
    public bool HasSecondary => Secondary is not null;
    public bool IsDanger { get; }
    public ICommand PrimaryCommand { get; }
    public ICommand SecondaryCommand { get; }
    public Task<bool> Result => _done.Task;
}

/// <summary>
/// The main screen «Спецификация изменений»: mark rows, then apply them in one go. Apply shows what will change, creates a restore
/// point before the first apply and backs up every value; «Вернуть всё как было» rolls back everything this app changed from those backups.
/// </summary>
public sealed class SpecViewModel : ObservableObject
{
    private readonly AppServices _s;
    private SpecProfile? _profile;
    private SpecDialogVm? _dialog;
    private bool _busy;
    private GtaInfo? _gta;
    private bool _gtaLooked;
    private bool _showUnsuitable;

    public SpecViewModel(AppServices services)
    {
        _s = services;
        Games = new ObservableCollection<SpecGameVm>(SpecCatalog.Games.Select(g => new SpecGameVm(g, Select)));
        ApplyCommand = new AsyncCommand(ApplyAsync, () => !IsBusy && MarkedCount > 0);
        RevertCommand = new AsyncCommand(RevertAllAsync, () => !IsBusy);
        ToggleUnsuitableCommand = new RelayCommand(() =>
        {
            _showUnsuitable = !_showUnsuitable;
            BuildItems();
        });
        _s.ChangesApplied += () => _ = RefreshAsync(keepMarks: true);
    }

    public ObservableCollection<SpecGameVm> Games { get; }
    public List<SpecRowVm> Rows { get; } = new();

    /// <summary>What the table shows: group headings and the rows that apply to this PC (or all, with «Показать неподходящие»).</summary>
    public ObservableCollection<object> Items { get; } = new();

    public ICommand ApplyCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand ToggleUnsuitableCommand { get; }

    public int UnsuitableCount => Rows.Count(r => r.IsUnsuitable);
    public bool HasUnsuitable => UnsuitableCount > 0;
    public string UnsuitableText => _showUnsuitable ? $"Скрыть неподходящие этому ПК ({UnsuitableCount})" : $"Показать неподходящие этому ПК ({UnsuitableCount})";

    public int MarkedCount => Rows.Count(r => r.IsMarked);
    public string Counter => MarkedCount.ToString("00");
    public string Total => Rows.Count(r => _showUnsuitable || !r.IsUnsuitable).ToString("00");
    public string ApplyText => $"Применить изменения ({MarkedCount})";
    public bool RiskMarked => Rows.Any(r => r.IsMarked && r.Risk == SpecRisk.Hard);

    public SpecDialogVm? Dialog
    {
        get => _dialog;
        private set
        {
            if (Set(ref _dialog, value))
            {
                OnPropertyChanged(nameof(HasDialog));
            }
        }
    }

    public bool HasDialog => _dialog is not null;

    public bool IsBusy
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

    public async Task LoadAsync()
    {
        if (_profile is null)
        {
            Select(Games[0]);
        }

        await Task.CompletedTask;
    }

    private async void Select(SpecGameVm game)
    {
        foreach (var g in Games)
        {
            g.IsSelected = ReferenceEquals(g, game);
        }

        if (game.Game == SpecGame.GtaRp && !_gtaLooked)
        {
            _gtaLooked = true;
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _gta = await Task.Run(() => new GtaLocator(_s.Fs, _s.Registry, documents).Locate(SystemScanner.IsProcessRunning));
        }

        _profile = SpecCatalog.For(game.Game, _gta);
        Rows.Clear();
        Rows.AddRange(_profile.Items.Select(i => new SpecRowVm(i, Changed)));
        BuildItems();
        await RefreshAsync(keepMarks: false);
    }

    /// <summary>Rebuilds the table: headings for groups with visible rows, rows numbered in the order shown.</summary>
    private void BuildItems()
    {
        Items.Clear();
        var n = 1;
        foreach (var group in Rows.GroupBy(r => r.Item.Group))
        {
            var visible = group.Where(r => _showUnsuitable || !r.IsUnsuitable).ToList();
            if (visible.Count == 0)
            {
                continue;
            }

            Items.Add(new SpecGroupVm(group.Key));
            foreach (var r in visible)
            {
                r.Number = (n++).ToString("00");
                Items.Add(r);
            }
        }

        Changed();
    }

    /// <summary>Re-reads every row; a fresh profile gets its default marks (only rows that can still be applied).</summary>
    private async Task RefreshAsync(bool keepMarks)
    {
        if (_profile is null || Rows.Count == 0)
        {
            return;
        }

        var profile = _profile;
        var rows = Rows.ToList();
        try
        {
            var snapshot = await Task.Run(_s.Rescan);
            _s.Snapshot = snapshot;
            var ctx = _s.Context(snapshot);
            var states = await Task.Run(() => rows.Select(r => TweakEngine.Evaluate(r.Item.Tweak, ctx, _s.Store)).ToList());
            if (!ReferenceEquals(profile, _profile))
            {
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Update(states[i]);
                if (!keepMarks)
                {
                    var wanted = profile.Defaults?.Contains(rows[i].Item.Id) ?? states[i].Plan.DefaultSelected;
                    rows[i].IsMarked = wanted && rows[i].CanMark;
                }
            }

            BuildItems();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Не удалось проверить состояние пунктов главного экрана", ex);
        }

        Changed();
    }

    /// <summary>A row was (un)marked: rows that set the same thing another way are unmarked.</summary>
    private void Changed(SpecRowVm row)
    {
        if (row.IsMarked && SpecCatalog.Exclusive.FirstOrDefault(g => g.Contains(row.Item.Id)) is { } group)
        {
            foreach (var other in Rows.Where(r => !ReferenceEquals(r, row) && r.IsMarked && group.Contains(r.Item.Id)))
            {
                other.IsMarked = false;
            }
        }

        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(UnsuitableCount));
        OnPropertyChanged(nameof(HasUnsuitable));
        OnPropertyChanged(nameof(UnsuitableText));
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(Counter));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ApplyText));
        OnPropertyChanged(nameof(RiskMarked));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task<bool> AskAsync(SpecDialogVm dialog)
    {
        Dialog = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            Dialog = null;
        }
    }

    private async Task ApplyAsync()
    {
        var marked = Rows.Where(r => r.IsMarked && r.State is { CanTurnOn: true }).ToList();
        if (marked.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var r in marked.Where(r => r.Item.Tweak.RequiresConsent))
            {
                var ok = await AskAsync(new SpecDialogVm("РИСК · ПУНКТ " + r.Number, r.Name + "?",
                    $"{r.Item.Tweak.Risk}. {r.Item.Tweak.Explanation}", Array.Empty<SpecDialogLine>(), "Я понимаю риск", "Не применять", danger: true));
                if (!ok)
                {
                    r.IsMarked = false;
                    marked.Remove(r);
                }
            }

            if (marked.Count == 0)
            {
                return;
            }

            var plans = marked.Select(r => r.State!.Plan).ToList();
            var preview = await Task.Run(() => marked.Select(r => (Row: r, Lines: _s.Executor.Preview(r.State!.Plan.Actions))).ToList());
            var firstApply = !RestorePoint.CreatedBefore && _s.Store.Load().Count == 0;
            var admin = plans.Any(p => p.RequiresAdmin) || firstApply;
            var lines = preview.SelectMany(p => p.Lines.Select((l, i) => new SpecDialogLine(i == 0 ? p.Row.Number : string.Empty,
                i == 0 ? p.Row.Name : l.Description, $"{l.Target}\n{l.Before} → {l.After}"))).ToList();
            var notes = new List<string> { "Прежнее значение каждого пункта сохраняется в копию до изменения." };
            if (firstApply)
            {
                notes.Add("Это первое применение: сначала будет создана точка восстановления Windows.");
            }

            if (admin && !AppServices.Elevated.IsActive)
            {
                notes.Add("Нужны права администратора: Windows спросит один раз за сеанс.");
            }

            if (marked.Any(r => r.Item.Tweak.RequiresRestart))
            {
                notes.Add("Часть пунктов заработает после перезагрузки.");
            }

            var go = await AskAsync(new SpecDialogVm("ПОДТВЕРЖДЕНИЕ", $"Применить {Plural(marked.Count)}?", string.Join(" ", notes), lines, "Применить", "Отмена"));
            if (!go)
            {
                return;
            }

            foreach (var r in marked)
            {
                r.Error = null;
            }

            var result = await _s.ApplyPlansAsync(plans, firstApply);
            _s.History.Add(new HistoryEntry(DateTime.Now, "Главный экран", $"Применено пунктов: {marked.Count}", string.Join("; ", result.Select(l => l.Text))));
            foreach (var line in result.Where(l => l.Ok == false && l.TweakId is not null))
            {
                if (marked.FirstOrDefault(r => r.Item.Id == line.TweakId) is { } row)
                {
                    row.Error = line.Error ?? "не удалось применить";
                }
            }

            _s.NotifyChangesApplied();
            await RefreshAsync(keepMarks: true);

            var report = new List<SpecDialogLine>();
            foreach (var line in result.Where(l => l.TweakId is null && l.Text.Length > 0 && !l.Text.StartsWith("Запрашиваю", StringComparison.Ordinal)))
            {
                report.Add(new SpecDialogLine(line.Text.StartsWith("✓", StringComparison.Ordinal) ? "✓" : "!", line.Text.TrimStart('✓', '⚠', ' '), Bad: !line.Text.StartsWith("✓", StringComparison.Ordinal)));
            }

            foreach (var r in marked)
            {
                var ok = result.Any(l => l.TweakId == r.Item.Id && l.Ok == true);
                report.Add(new SpecDialogLine(ok ? "✓" : "✗", r.Name, ok ? null : r.Error, Bad: !ok));
            }

            var reboot = marked.Any(r => r.Item.Tweak.RequiresRestart && result.Any(l => l.TweakId == r.Item.Id && l.Ok == true));
            var failed = report.Count(l => l.Mark == "✗");
            await AskAsync(new SpecDialogVm("РЕЗУЛЬТАТ", failed == 0 ? "Изменения применены" : $"Не применено: {failed} из {marked.Count}",
                (failed > 0 ? "Остальные пункты применены, ничего не откачено молча. Ошибки отмечены на строках. " : string.Empty) +
                (reboot ? "Перезагрузите компьютер, чтобы все изменения вступили в силу." : string.Empty),
                report, "Готово", null));
        }
        finally
        {
            IsBusy = false;
            Changed();
        }
    }

    private async Task RevertAllAsync()
    {
        IsBusy = true;
        try
        {
            var sets = _s.Store.Active().Where(c => !c.Failed).OrderByDescending(c => c.CreatedUtc).ToList();
            if (sets.Count == 0)
            {
                await AskAsync(new SpecDialogVm("ОТКАТ", "Откатывать нечего", "Программа ещё ничего не меняла на этом компьютере (или всё уже возвращено).",
                    Array.Empty<SpecDialogLine>(), "Понятно", null));
                return;
            }

            var lines = sets.Select((c, i) => new SpecDialogLine((i + 1).ToString("00"), c.Title, c.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))).ToList();
            var admin = sets.Any(c => c.RequiresAdminToRollback) && !Elevation.IsElevated && !AppServices.Elevated.IsActive;
            var go = await AskAsync(new SpecDialogVm("ОТКАТ", "Вернуть всё как было?",
                "Программа вернёт прежние значения из своих копий — от новых изменений к старым. Затрагивается только то, что меняла FrameLab." +
                (admin ? " Для системных настроек Windows спросит права администратора." : string.Empty),
                lines, "Вернуть всё", "Отмена"));
            if (!go)
            {
                return;
            }

            if (sets.Select(AppServices.GameOrSteamFileInvolved).FirstOrDefault(b => b is not null) is { } blocker)
            {
                await AskAsync(new SpecDialogVm("ОТКАТ", "Сначала закройте игру", blocker, Array.Empty<SpecDialogLine>(), "Понятно", null));
                return;
            }

            var outcome = new Dictionary<string, (bool Ok, string? Error)>();
            var adminIds = sets.Where(c => c.RequiresAdminToRollback && !Elevation.IsElevated).Select(c => c.Id).ToList();
            if (adminIds.Count > 0)
            {
                var r = await Task.Run(() => AppServices.RunElevated(new ElevatedRequest { Mode = "rollback", ChangeSetIds = adminIds }));
                foreach (var id in adminIds)
                {
                    var item = r?.Items.FirstOrDefault(i => i.ChangeSetId == id);
                    outcome[id] = item is null ? (false, "права администратора не выданы") : (item.Success, item.Success ? null : "не все значения удалось вернуть, подробности в журнале");
                }
            }

            foreach (var set in sets.Where(c => !adminIds.Contains(c.Id)))
            {
                var r = await Task.Run(() => _s.Executor.Rollback(set.Id));
                outcome[set.Id] = (r.Success, r.Success ? null : string.Join("; ", r.Messages));
            }

            _s.History.Add(new HistoryEntry(DateTime.Now, "Откат", "Вернуть всё как было", string.Join("; ", outcome.Select(o => $"{o.Key}: {(o.Value.Ok ? "ок" : o.Value.Error)}"))));
            _s.NotifyChangesApplied();
            await RefreshAsync(keepMarks: false);

            var report = sets.Select(c => outcome.TryGetValue(c.Id, out var o) && o.Ok
                ? new SpecDialogLine("✓", c.Title)
                : new SpecDialogLine("✗", c.Title, outcome.TryGetValue(c.Id, out var e) ? e.Error : "не выполнено", Bad: true)).ToList();
            var failed = report.Count(l => l.Bad);
            var reboot = sets.Any(c => c.Records.Any(rec => rec.Target.Contains("HwSchMode", StringComparison.OrdinalIgnoreCase) ||
                                                           rec.Target.Contains("DeviceGuard", StringComparison.OrdinalIgnoreCase) ||
                                                           rec.Target.Contains("OverlayTestMode", StringComparison.OrdinalIgnoreCase) ||
                                                           rec.Target.Contains("GlobalTimerResolutionRequests", StringComparison.OrdinalIgnoreCase)));
            await AskAsync(new SpecDialogVm("РЕЗУЛЬТАТ", failed == 0 ? "Всё возвращено" : $"Не возвращено: {failed} из {sets.Count}",
                (failed > 0 ? "Не вернувшиеся пункты остались в журнале — можно повторить. " : string.Empty) +
                (reboot ? "Перезагрузите компьютер, чтобы возврат системных настроек вступил в силу." : string.Empty),
                report, "Готово", null));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Plural(int n) => n % 10 == 1 && n % 100 != 11 ? $"{n} изменение"
        : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? $"{n} изменения"
        : $"{n} изменений";
}
