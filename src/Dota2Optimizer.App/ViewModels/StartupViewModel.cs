using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.App.Views;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

public sealed class StartupRowVm : ObservableObject
{
    private readonly StartupViewModel _owner;
    private bool _busy;
    private MarkState _mark;

    public StartupRowVm(StartupViewModel owner, StartupEntry entry, bool ours, bool exists)
    {
        _owner = owner;
        Entry = entry;
        ChangedByUs = ours;
        FileMissing = !exists;
        ToggleCommand = new AsyncCommand(() => _owner.ToggleAsync(this), () => !IsBusy);
    }

    public StartupEntry Entry { get; }
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Source => Entry.Source;
    public bool IsOn => Entry.Enabled;
    public bool RequiresAdmin => Entry.RequiresAdmin;
    public bool ChangedByUs { get; }
    public bool FileMissing { get; }
    public ICommand ToggleCommand { get; }

    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                if (value)
                {
                    Mark = MarkState.Busy;
                }

                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public MarkState Mark
    {
        get => _mark;
        set => Set(ref _mark, value);
    }
}

/// <summary>«Автозагрузка»: programs that start with Windows, switched like in Task Manager.</summary>
public sealed class StartupViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly StartupManager _manager;
    private bool _loading;
    private string? _status;

    public StartupViewModel(AppServices services)
    {
        _s = services;
        _manager = StartupManager.ForCurrentUser(services.Registry, services.Fs);
        RefreshCommand = new AsyncCommand(LoadAsync, () => !IsLoading);
    }

    public ObservableCollection<StartupRowVm> Entries { get; } = new();
    public ICommand RefreshCommand { get; }

    public bool IsLoading
    {
        get => _loading;
        private set => Set(ref _loading, value);
    }

    public string? Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string Summary => Entries.Count == 0 ? "Список пуст" : $"Включено {Entries.Count(e => e.IsOn)} из {Entries.Count}";

    public Task ActivateAsync() => LoadAsync();

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var rows = await Task.Run(() => _manager.List()
                .Select(e => (Entry: e, Ours: TweakEngine.OwnChanges(e.TweakId, _s.Store).Count > 0, Exists: e.ExePath.Length == 0 || File.Exists(Environment.ExpandEnvironmentVariables(e.ExePath))))
                .ToList());
            Entries.Clear();
            foreach (var (entry, ours, exists) in rows)
            {
                Entries.Add(new StartupRowVm(this, entry, ours, exists));
            }

            OnPropertyChanged(nameof(Summary));
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task ToggleAsync(StartupRowVm row)
    {
        row.IsBusy = true;
        var done = false;
        try
        {
            if (row.ChangedByUs)
            {
                done = await _s.RevertTweakAsync(row.Entry.TweakId, $"Автозагрузка: {row.Name}") is not null;
            }
            else
            {
                var tweak = new StartupEntryTweak(row.Entry, enable: !row.IsOn);
                var plan = tweak.Plan(_s.Context(_s.Snapshot ?? new Core.SystemInfo.SystemSnapshot()));
                var lines = await _s.ConfirmAndApplyAsync(new[] { plan }, tweak.Title);
                done = lines is not null;
                if (lines is not null)
                {
                    Status = string.Join("\n", lines.Select(l => l.Text));
                }
            }
        }
        finally
        {
            row.IsBusy = false;
        }

        if (done)
        {
            await LoadAsync();
            var updated = Entries.FirstOrDefault(e => e.Entry.TweakId == row.Entry.TweakId);
            if (updated is not null)
            {
                updated.Mark = updated.IsOn ? MarkState.Undone : MarkState.Done;
            }
        }
        else
        {
            row.Mark = MarkState.None;
        }
    }
}
