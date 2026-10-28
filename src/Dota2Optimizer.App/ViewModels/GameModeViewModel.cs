using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Session;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>A preset of background programs/services the user can switch on as a whole.</summary>
public sealed class PresetVm : ObservableObject
{
    private readonly GameModeViewModel _owner;

    public PresetVm(GameModeViewModel owner, SessionPreset preset)
    {
        _owner = owner;
        Preset = preset;
    }

    public SessionPreset Preset { get; }
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string Items => string.Join(", ", Preset.Processes.Concat(Preset.Services));

    public bool IsOn
    {
        get => _owner.HasAll(Preset);
        set
        {
            _owner.SetPreset(Preset, value);
            OnPropertyChanged();
        }
    }

    public void Raise() => OnPropertyChanged(nameof(IsOn));
}

/// <summary>A game the user added by hand, with its own fullscreen-optimisation switch.</summary>
public sealed class WatchedGameVm
{
    public WatchedGameVm(WatchedGame game, TweakRowVm? fso)
    {
        Game = game;
        Fso = fso;
    }

    public WatchedGame Game { get; }
    public string Name => Game.Name;
    public string Detail => Game.Path ?? Game.Process + ".exe";
    public TweakRowVm? Fso { get; }
}

/// <summary>«Игровой режим»: what is paused while a game runs, and the live state of the current session.</summary>
public sealed class GameModeViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly HomeViewModel _home;
    private readonly GameSessionManager _manager;
    private readonly GameModeConfig _config;
    private readonly DispatcherTimer _timer;
    private bool _ticking;
    private string _state = "Игра не запущена.";
    private string _newProcess = string.Empty;
    private string _newService = string.Empty;

    public GameModeViewModel(AppServices services, HomeViewModel home)
    {
        _s = services;
        _home = home;
        _manager = new GameSessionManager(new ProcessControl(), new ServiceControl(services.Registry, services.Commands), services.Fs, AppPaths.GameSession);
        _config = GameModeConfigStore.Load(services.Fs, AppPaths.GameModeConfig);
        Presets = GameModePresets.All.Select(p => new PresetVm(this, p)).ToList();
        Processes = new ObservableCollection<string>(_config.Processes);
        ServicesList = new ObservableCollection<string>(_config.Services);
        Games = new ObservableCollection<WatchedGameVm>(_config.Games.Select(Wrap));
        AddGameCommand = new RelayCommand(AddGame);
        RemoveGameCommand = new RelayCommand(p => RemoveGame(p as WatchedGameVm));
        AddProcessCommand = new RelayCommand(() => AddTo(Processes, _config.Processes, NewProcess, p => NewProcess = p), () => NewProcess.Trim().Length > 0);
        RemoveProcessCommand = new RelayCommand(p => RemoveFrom(Processes, _config.Processes, p as string));
        AddServiceCommand = new RelayCommand(() => AddTo(ServicesList, _config.Services, NewService, v => NewService = v), () => NewService.Trim().Length > 0);
        RemoveServiceCommand = new RelayCommand(p => RemoveFrom(ServicesList, _config.Services, p as string));
        RestoreNowCommand = new AsyncCommand(RestoreNowAsync, () => _manager.Active);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += async (_, _) => await TickAsync();
        _ = RecoverAsync();
    }

    public IReadOnlyList<PresetVm> Presets { get; }
    public ObservableCollection<string> Processes { get; }
    public ObservableCollection<string> ServicesList { get; }
    public ObservableCollection<WatchedGameVm> Games { get; }
    public string KnownGames => string.Join(" · ", GameModePresets.KnownGames.Select(g => g.Name).Distinct());
    public bool IsElevated => Elevation.IsElevated;
    public ICommand AddGameCommand { get; }
    public ICommand RemoveGameCommand { get; }
    public ICommand AddProcessCommand { get; }
    public ICommand RemoveProcessCommand { get; }
    public ICommand AddServiceCommand { get; }
    public ICommand RemoveServiceCommand { get; }
    public ICommand RestoreNowCommand { get; }

    public bool Enabled
    {
        get => _config.Enabled;
        set
        {
            _config.Enabled = value;
            Save();
            OnPropertyChanged();
            UpdateTimer();
            if (!value && _manager.Active)
            {
                _ = RestoreNowAsync();
            }
        }
    }

    public bool HighPriority
    {
        get => _config.HighPriority;
        set
        {
            _config.HighPriority = value;
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The power plan part lives on the Home page («Ускорение на время игры»); this is the same switch.</summary>
    public bool PowerBoost
    {
        get => _home.BoostEnabled;
        set
        {
            _home.BoostEnabled = value;
            OnPropertyChanged();
        }
    }

    public string State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public string NewProcess
    {
        get => _newProcess;
        set => Set(ref _newProcess, value ?? string.Empty);
    }

    public string NewService
    {
        get => _newService;
        set => Set(ref _newService, value ?? string.Empty);
    }

    public Task ActivateAsync() => RefreshFsoAsync();

    public void Shutdown()
    {
        _timer.Stop();
        if (_manager.Active)
        {
            // Closing the app ends the session: put everything back now rather than on the next start.
            _manager.Restore("FrameLab закрыт");
        }
    }

    internal bool HasAll(SessionPreset p) =>
        p.Processes.All(x => _config.Processes.Contains(x, StringComparer.OrdinalIgnoreCase)) &&
        p.Services.All(x => _config.Services.Contains(x, StringComparer.OrdinalIgnoreCase));

    internal void SetPreset(SessionPreset p, bool on)
    {
        Toggle(Processes, _config.Processes, p.Processes, on);
        Toggle(ServicesList, _config.Services, p.Services, on);
        Save();
    }

    private static void Toggle(ObservableCollection<string> view, List<string> list, IEnumerable<string> items, bool on)
    {
        foreach (var item in items)
        {
            var has = list.Contains(item, StringComparer.OrdinalIgnoreCase);
            if (on && !has)
            {
                list.Add(item);
                view.Add(item);
            }
            else if (!on && has)
            {
                list.RemoveAll(x => x.Equals(item, StringComparison.OrdinalIgnoreCase));
                var v = view.FirstOrDefault(x => x.Equals(item, StringComparison.OrdinalIgnoreCase));
                if (v is not null)
                {
                    view.Remove(v);
                }
            }
        }
    }

    private void AddTo(ObservableCollection<string> view, List<string> list, string raw, Action<string> clear)
    {
        var name = raw.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (name.Length == 0 || list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            clear(string.Empty);
            return;
        }

        if (GameModePresets.Protected.Contains(name) || GameModePresets.ProtectedServices.Contains(name))
        {
            Ui.Info($"«{name}» нельзя останавливать во время игры: это часть Windows, голосовой чат, лаунчер, античит или защита. Программа его пропустит.");
            clear(string.Empty);
            return;
        }

        list.Add(name);
        view.Add(name);
        clear(string.Empty);
        Save();
        RaisePresets();
    }

    private void RemoveFrom(ObservableCollection<string> view, List<string> list, string? name)
    {
        if (name is null)
        {
            return;
        }

        list.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        view.Remove(name);
        Save();
        RaisePresets();
    }

    private void AddGame()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Игра (*.exe)|*.exe", Title = "Выберите exe игры" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var process = Path.GetFileNameWithoutExtension(dlg.FileName);
        if (_config.Games.Any(g => g.Process.Equals(process, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(dlg.FileName);
        var game = new WatchedGame
        {
            Process = process,
            Path = dlg.FileName,
            Name = string.IsNullOrWhiteSpace(info.FileDescription) ? process : info.FileDescription.Trim(),
        };
        _config.Games.Add(game);
        Games.Add(Wrap(game));
        Save();
        _ = RefreshFsoAsync();
    }

    private void RemoveGame(WatchedGameVm? vm)
    {
        if (vm is null)
        {
            return;
        }

        _config.Games.Remove(vm.Game);
        Games.Remove(vm);
        Save();
    }

    private WatchedGameVm Wrap(WatchedGame g) =>
        new(g, g.Path is null ? null : new TweakRowVm(new FsoExeTweak(g.Path), ToggleFsoAsync));

    private async Task ToggleFsoAsync(TweakRowVm row)
    {
        if (row.State is not { } st)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            if (st.CanTurnOff)
            {
                row.Mark = await _s.RevertTweakAsync(row.Tweak.Id, row.Title) is null ? row.Mark : Views.MarkState.Undone;
            }
            else if (st.CanTurnOn)
            {
                row.Mark = await _s.ConfirmAndApplyAsync(new[] { st.Plan }, row.Title) is null ? row.Mark : Views.MarkState.Done;
            }
        }
        finally
        {
            row.IsBusy = false;
            await RefreshFsoAsync();
        }
    }

    private async Task RefreshFsoAsync()
    {
        var rows = Games.Select(g => g.Fso).OfType<TweakRowVm>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var ctx = _s.Context(_s.Snapshot ?? new Core.SystemInfo.SystemSnapshot());
        var states = await Task.Run(() => rows.Select(r => TweakEngine.Evaluate(r.Tweak, ctx, _s.Store)).ToList());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Update(states[i]);
        }
    }

    private async Task RecoverAsync()
    {
        var messages = await Task.Run(_manager.RecoverAfterCrash);
        if (messages.Count > 0)
        {
            State = string.Join(" ", messages);
        }

        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (_config.Enabled)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private async Task TickAsync()
    {
        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            var snapshot = Clone(_config);
            var messages = await Task.Run(() => _manager.Tick(snapshot));
            if (messages.Count > 0)
            {
                State = string.Join(" ", messages);
            }
            else if (!_manager.Active && !State.StartsWith("Игра закрыта", StringComparison.Ordinal) && !State.StartsWith("Восстановление", StringComparison.Ordinal))
            {
                State = "Игра не запущена — программа ждёт запуска игры из списка.";
            }

            CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task RestoreNowAsync()
    {
        var messages = await Task.Run(() => _manager.Restore("Возвращено вручную"));
        State = messages.Count > 0 ? string.Join(" ", messages) : "Нечего возвращать.";
        CommandManager.InvalidateRequerySuggested();
    }

    private static GameModeConfig Clone(GameModeConfig c) => new()
    {
        Enabled = c.Enabled,
        HighPriority = c.HighPriority,
        Games = c.Games.ToList(),
        Processes = c.Processes.ToList(),
        Services = c.Services.ToList(),
    };

    private void RaisePresets()
    {
        foreach (var p in Presets)
        {
            p.Raise();
        }
    }

    private void Save()
    {
        try
        {
            GameModeConfigStore.Save(_s.Fs, AppPaths.GameModeConfig, _config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось сохранить настройки игрового режима: {ex.Message}");
        }
    }
}
