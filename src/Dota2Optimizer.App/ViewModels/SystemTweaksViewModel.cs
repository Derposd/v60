using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dota2Optimizer.App.Views;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>One tweak shown as a switch.</summary>
public sealed class TweakRowVm : ObservableObject
{
    private readonly Func<TweakRowVm, Task> _toggle;
    private TweakToggle? _state;
    private bool _busy;
    private MarkState _mark;

    public TweakRowVm(Tweak tweak, Func<TweakRowVm, Task> toggle)
    {
        Tweak = tweak;
        _toggle = toggle;
        ToggleCommand = new AsyncCommand(() => _toggle(this), () => !IsBusy && CanToggle);
    }

    public Tweak Tweak { get; }
    public TweakToggle? State => _state;
    public TweakCategory Category => Tweak.Category;
    public string Title => Tweak.Title;
    public string Explanation => Tweak.Explanation;
    public string Effect => "Эффект: " + Tweak.Effectiveness.Ru().ToLowerInvariant();
    public Effectiveness EffectValue => Tweak.Effectiveness;
    public TweakRisk RiskValue => Tweak.RiskLevel;
    public string RiskLabel => Tweak.RiskLevel.Ru();
    public bool IsRecommended => Tweak.Recommended && !Tweak.Disputed;
    public bool IsDisputed => Tweak.Disputed;
    public bool RequiresRestart => Tweak.RequiresRestart;
    public string? AppliesTo => Tweak.AppliesTo;
    public string? ManualSteps => _state?.Plan.Status == TweakStatus.ManualOnly ? Tweak.ManualSteps : null;
    public bool RequiresAdmin => _state?.Plan.RequiresAdmin ?? false;
    public bool IsOn => _state?.IsOn ?? false;
    public bool CanToggle => _state?.CanToggle ?? false;
    public string Note => _state?.Note ?? "Проверяю…";
    public string SwitchTip => Tweak.Risk;
    public ICommand ToggleCommand { get; }

    public Brush NoteBrush => (Brush)Application.Current.Resources[_state switch
    {
        { CanTurnOff: true } => "Ok",
        { Plan.Status: TweakStatus.Blocked } => "Warn",
        _ => "Subtle",
    }];

    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                Mark = value ? MarkState.Busy : Mark;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public MarkState Mark
    {
        get => _mark;
        set
        {
            if (Set(ref _mark, value))
            {
                OnPropertyChanged(nameof(MarkBrush));
            }
        }
    }

    public Brush MarkBrush => (Brush)Application.Current.Resources[Mark switch
    {
        MarkState.Error => "Crit",
        MarkState.Undone => "Muted",
        MarkState.Busy => "AccentHover",
        _ => "Ok",
    }];

    public bool Matches(string query) =>
        query.Length == 0 || Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || Explanation.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    public void Update(TweakToggle state)
    {
        _state = state;
        foreach (var p in new[] { nameof(State), nameof(IsOn), nameof(CanToggle), nameof(Note), nameof(NoteBrush), nameof(RequiresAdmin), nameof(ManualSteps) })
        {
            OnPropertyChanged(p);
        }

        CommandManager.InvalidateRequerySuggested();
    }
}

public sealed class TweakGroupVm : ObservableObject
{
    private bool _visible = true;

    public TweakGroupVm(TweakCategory category, IEnumerable<TweakRowVm> rows)
    {
        Category = category;
        Rows = new ObservableCollection<TweakRowVm>(rows);
        Visible = new ObservableCollection<TweakRowVm>(Rows);
    }

    public TweakCategory Category { get; }
    public string Title => Category.Ru();
    public ObservableCollection<TweakRowVm> Rows { get; }
    public ObservableCollection<TweakRowVm> Visible { get; }
    public string Counter => $"{Rows.Count(r => r.IsOn)} из {Rows.Count} включено";

    public bool IsVisible
    {
        get => _visible;
        private set => Set(ref _visible, value);
    }

    public void Filter(string query, TweakCategory? only)
    {
        Visible.Clear();
        foreach (var r in Rows.Where(r => r.Matches(query) && (only is null || only == Category)))
        {
            Visible.Add(r);
        }

        IsVisible = Visible.Count > 0;
    }

    public void RaiseCounter() => OnPropertyChanged(nameof(Counter));
}

/// <summary>«Твики Windows»: system, interface, privacy and service tweaks as switches, grouped by category.</summary>
public sealed class SystemTweaksViewModel : ObservableObject
{
    private static readonly TweakCategory[] Order = { TweakCategory.Windows, TweakCategory.Gpu, TweakCategory.Interface, TweakCategory.Privacy, TweakCategory.Services };

    private readonly AppServices _s;
    private bool _loading;
    private bool _loaded;
    private string _search = string.Empty;
    private TweakCategory? _only;
    private string _status = string.Empty;

    public SystemTweaksViewModel(AppServices services)
    {
        _s = services;
        Groups = new ObservableCollection<TweakGroupVm>(Order.Select(c =>
            new TweakGroupVm(c, SystemTweakCatalog.All.Where(t => t.Category == c).Select(t => new TweakRowVm(t, ToggleAsync)))));
        RefreshCommand = new AsyncCommand(() => LoadAsync(force: true), () => !IsLoading);
        ApplyRecommendedCommand = new AsyncCommand(ApplyRecommendedAsync, () => !IsLoading && Recommended().Any());
        FilterCommand = new RelayCommand(p => Only = p is TweakCategory c ? c : null);
        _s.ChangesApplied += () => Application.Current.Dispatcher.BeginInvoke(() => _ = LoadAsync(force: true));
    }

    public ObservableCollection<TweakGroupVm> Groups { get; }
    public IReadOnlyList<TweakCategory> Categories => Order;
    public ICommand RefreshCommand { get; }
    public ICommand ApplyRecommendedCommand { get; }
    public ICommand FilterCommand { get; }

    public bool IsLoading
    {
        get => _loading;
        private set => Set(ref _loading, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public TweakCategory? Only
    {
        get => _only;
        set
        {
            if (Set(ref _only, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(IsAll));
            }
        }
    }

    public bool IsAll => Only is null;

    public string Summary
    {
        get
        {
            var rows = Groups.SelectMany(g => g.Rows).ToList();
            return $"Включено {rows.Count(r => r.IsOn)} из {rows.Count}; программой — {rows.Count(r => r.State?.CanTurnOff == true)}";
        }
    }

    public Task ActivateAsync() => _loaded ? Task.CompletedTask : LoadAsync(force: false);

    public async Task LoadAsync(bool force)
    {
        if (IsLoading || (_loaded && !force))
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await _s.EnsureSnapshotAsync();
            var ctx = _s.Context(snapshot);
            var rows = Groups.SelectMany(g => g.Rows).ToList();
            var states = await Task.Run(() => rows.Select(r => TweakEngine.Evaluate(r.Tweak, ctx, _s.Store)).ToList());
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Update(states[i]);
            }

            _loaded = true;
            Raise();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "Не удалось проверить твики: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private IEnumerable<TweakRowVm> Recommended() =>
        Groups.SelectMany(g => g.Rows).Where(r => r.IsRecommended && r.State is { CanTurnOn: true });

    private async Task ApplyRecommendedAsync()
    {
        var rows = Recommended().ToList();
        foreach (var r in rows)
        {
            r.IsBusy = true;
        }

        try
        {
            var lines = await _s.ConfirmAndApplyAsync(rows.Select(r => r.State!.Plan).ToList(), $"Рекомендуемые твики: {rows.Count}");
            Status = lines is null ? "Отменено." : string.Join("\n", lines.Select(l => l.Text));
            await ReevaluateAsync(rows, lines is null ? null : MarkState.Done);
        }
        finally
        {
            foreach (var r in rows)
            {
                r.IsBusy = false;
            }
        }
    }

    private async Task ToggleAsync(TweakRowVm row)
    {
        if (row.State is not { } state)
        {
            return;
        }

        row.IsBusy = true;
        MarkState? result = null;
        try
        {
            if (state.CanTurnOff)
            {
                var messages = await _s.RevertTweakAsync(row.Tweak.Id, row.Title);
                if (messages is not null)
                {
                    Status = $"«{row.Title}» выключено: " + string.Join("; ", messages);
                    result = messages.Any(m => m.StartsWith("Ошибка", StringComparison.Ordinal)) ? MarkState.Error : MarkState.Undone;
                }
            }
            else if (state.CanTurnOn)
            {
                var lines = await _s.ConfirmAndApplyAsync(new[] { state.Plan }, row.Title);
                if (lines is not null)
                {
                    Status = string.Join("\n", lines.Select(l => l.Text));
                    result = lines.Any(l => l.Text.StartsWith("✗", StringComparison.Ordinal) || l.Text.StartsWith("Пропущено", StringComparison.Ordinal))
                        ? MarkState.Error
                        : MarkState.Done;
                }
            }

            await ReevaluateAsync(new[] { row }, result);
        }
        finally
        {
            row.IsBusy = false;
            if (result is { } r)
            {
                row.Mark = r;
            }
            else if (row.Mark == MarkState.Busy)
            {
                row.Mark = MarkState.None;
            }
        }
    }

    /// <summary>Re-reads the state of the given rows; power plan changes need a fresh look at the PC.</summary>
    private async Task ReevaluateAsync(IReadOnlyList<TweakRowVm> rows, MarkState? mark)
    {
        var snapshot = rows.Any(r => r.Tweak.Id.Contains("powerplan", StringComparison.Ordinal))
            ? await Task.Run(_s.Rescan)
            : await _s.EnsureSnapshotAsync();
        if (!ReferenceEquals(snapshot, _s.Snapshot))
        {
            _s.Snapshot = snapshot;
        }

        var ctx = _s.Context(snapshot);
        var states = await Task.Run(() => rows.Select(r => TweakEngine.Evaluate(r.Tweak, ctx, _s.Store)).ToList());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Update(states[i]);
            if (mark is { } m)
            {
                rows[i].Mark = m;
            }
        }

        Raise();
    }

    private void ApplyFilter()
    {
        foreach (var g in Groups)
        {
            g.Filter(Search.Trim(), Only);
        }
    }

    private void Raise()
    {
        foreach (var g in Groups)
        {
            g.RaiseCounter();
        }

        OnPropertyChanged(nameof(Summary));
        CommandManager.InvalidateRequerySuggested();
    }
}
