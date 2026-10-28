using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.ViewModels;

public sealed class ChangeSetVm
{
    public ChangeSetVm(ChangeSet set)
    {
        Set = set;
    }

    public ChangeSet Set { get; }
    public string Title => Set.Title;
    public string When => Set.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string State => Set.Failed ? "ошибка, откачено автоматически" : Set.RolledBack ? $"откачено {Set.RolledBackUtc?.ToLocalTime():dd.MM HH:mm}" : "активно";
    public bool CanRollback => !Set.RolledBack;
    public string Details => string.Join("\n", Set.Records.Select(r => $"• {r.Description} — {r.Target}"));
    public string AdminText => Set.RequiresAdminToRollback ? "нужны права администратора" : string.Empty;
}

public sealed class RollbackViewModel : ObservableObject
{
    private readonly AppServices _s;
    private string _status = string.Empty;

    public RollbackViewModel(AppServices services)
    {
        _s = services;
        RollbackCommand = new AsyncCommand<ChangeSetVm>(RollbackOneAsync);
        RollbackAllCommand = new AsyncCommand(RollbackAllAsync, () => Sets.Any(s => s.CanRollback));
        RefreshCommand = new RelayCommand(Reload);
        OpenFolderCommand = new RelayCommand(() => Ui.OpenFolder(AppPaths.Backups));
        _s.ChangesApplied += () => System.Windows.Application.Current.Dispatcher.Invoke(Reload);
        Reload();
    }

    public ICommand RollbackCommand { get; }
    public ICommand RollbackAllCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ObservableCollection<ChangeSetVm> Sets { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public void Reload()
    {
        Sets.Clear();
        foreach (var s in _s.Store.Load().OrderByDescending(s => s.CreatedUtc))
        {
            Sets.Add(new ChangeSetVm(s));
        }

        History.Clear();
        foreach (var h in _s.History.Entries.OrderByDescending(h => h.Time).Take(200))
        {
            History.Add(h);
        }
    }

    private async Task RollbackOneAsync(ChangeSetVm? vm)
    {
        if (vm is null || !vm.CanRollback)
        {
            return;
        }

        var messages = await _s.UndoAsync(vm.Set.Id);
        if (messages is not null)
        {
            Status = string.Join("\n", messages);
        }
    }

    private async Task RollbackAllAsync()
    {
        if (!Ui.Confirm("Откатить ВСЕ изменения, сделанные программой (от новых к старым)?"))
        {
            return;
        }

        var active = _s.Store.Active();
        if (active.Select(AppServices.GameOrSteamFileInvolved).FirstOrDefault(b => b is not null) is { } blocker)
        {
            Ui.Info(blocker);
            return;
        }

        var messages = new List<string>();
        var adminIds = active.Where(a => a.RequiresAdminToRollback).OrderByDescending(a => a.CreatedUtc).Select(a => a.Id).ToList();
        if (adminIds.Count > 0 && !Elevation.IsElevated)
        {
            // Only admin-level sets go to the elevated copy: if UAC is confirmed by another administrator account,
            // that process has a different HKCU, so per-user values must be restored here.
            var result = await Task.Run(() => AppServices.RunElevated(new ElevatedRequest { Mode = "rollback", ChangeSetIds = adminIds }));
            messages.AddRange(result?.Messages ?? new List<string> { "Права администратора не выданы — системные изменения не откачены." });
        }

        messages.AddRange((await Task.Run(() => _s.Executor.RollbackAll(includeAdmin: Elevation.IsElevated))).Messages);

        _s.History.Add(new HistoryEntry(DateTime.Now, "Откат", "Откатить всё", string.Join("; ", messages)));
        Status = string.Join("\n", messages);
        _s.NotifyChangesApplied();
    }
}

public sealed class AsyncCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private bool _running;

    public AsyncCommand(Func<T?, Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        _running = true;
        try
        {
            await _execute(parameter is T t ? t : default);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка команды", ex);
            System.Windows.MessageBox.Show(ex.Message, "FrameLab");
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public sealed class AboutViewModel : ObservableObject
{
    private string _logText = string.Empty;

    public AboutViewModel()
    {
        OpenLogsCommand = new RelayCommand(() => Ui.OpenFolder(AppPaths.Logs));
        RefreshLogCommand = new RelayCommand(LoadLog);
        LoadLog();
    }

    public ICommand OpenLogsCommand { get; }
    public ICommand RefreshLogCommand { get; }

    public string Version => "Версия " + ShortVersion.TrimStart('v');

    public string ShortVersion => "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");
    public string DataFolder => AppPaths.Root;

    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    private void LoadLog()
    {
        try
        {
            if (!File.Exists(Log.CurrentFile))
            {
                LogText = "Лог за сегодня пуст.";
                return;
            }

            using var fs = new FileStream(Log.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var lines = reader.ReadToEnd().Split('\n');
            LogText = string.Join('\n', lines.TakeLast(300));
        }
        catch (IOException ex)
        {
            LogText = ex.Message;
        }
    }
}

public sealed class MainViewModel : ObservableObject
{
    // Tab order in MainWindow.xaml: home, a section header, the Dota 2 / CS2 pages, the GTA RP pages, the «ПК» pages (Windows and
    // other games) — only one group is visible — then a section header and the shared pages.
    public const int HomeTab = 0;
    public const int OptimizeTab = 2;
    public const int IssuesTab = 3;
    public const int MonitorTab = 4;
    public const int CrashTab = 5;
    public const int GtaModesTab = 6;
    public const int GtaReduxTab = 7;
    public const int GtaDiagnosisTab = 8;
    public const int SystemTweaksTab = 9;
    public const int GameModeTab = 10;
    public const int StartupTab = 11;
    public const int BoostTab = 12;
    public const int PresetsTab = 13;
    public const int BenchmarkTab = 14;
    public const int NetworkTab = 16;
    public const int RollbackTab = 17;

    private enum Section
    {
        Game,
        Gta,
        Pc,
    }

    private int _selectedTab;
    private GameWorkspace _current;
    private Section _section;

    public MainViewModel()
    {
        var services = new AppServices();
        Dota = new GameWorkspace(services, Core.Game.Games.Dota2, i => SelectedTab = i);
        Cs2 = new GameWorkspace(services, Core.Game.Games.Cs2, i => SelectedTab = i);
        Gta = new GtaViewModel(services);
        Network = new NetworkViewModel(services);
        Rollback = new RollbackViewModel(services);
        SystemTweaks = new SystemTweaksViewModel(services);
        Startup = new StartupViewModel(services);
        Boost = new BoostViewModel(services);
        Presets = new PresetsViewModel(services);
        Benchmark = new BenchmarkViewModel(services);
        About = new AboutViewModel();
        Settings = new SettingsViewModel(ReapplyTheme);
        Home = new HomeViewModel(services, Dota, Cs2, (ws, tab) =>
        {
            Select(ws);
            SelectedTab = tab;
        }, () =>
        {
            SelectGta();
            SelectedTab = GtaModesTab;
        }, () =>
        {
            SelectPc();
            SelectedTab = SystemTweaksTab;
        });
        GameMode = new GameModeViewModel(services, Home);
        SystemTweaks.GameMode = GameMode;
        // The app always opens on the «ПК» section; the Dota 2 / CS2 chip returns to the game opened last time.
        _current = UiState.LastGame() == Core.Game.GameId.Cs2 ? Cs2 : Dota;
        SelectPc();
        Live.Active = true; // the app opens on the home page
    }

    public HomeViewModel Home { get; }
    public DashboardViewModel Live { get; } = new();

    /// <summary>The window is minimised: live sampling pauses.</summary>
    public bool WindowHidden
    {
        set => Live.Active = !value && SelectedTab == HomeTab;
    }

    public GameWorkspace Dota { get; }
    public GameWorkspace Cs2 { get; }
    public GtaViewModel Gta { get; }

    /// <summary>The Steam game whose pages are shown (or were shown last, while the GTA RP section is open).</summary>
    public GameWorkspace Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    /// <summary>True while the GTA RP section is open: its pages replace the Dota 2 / CS2 ones.</summary>
    public bool IsGta
    {
        get => _section == Section.Gta;
        set
        {
            if (value)
            {
                SelectGta();
            }
        }
    }

    /// <summary>True while the «ПК» section is open: Windows tweaks, the game mode, presets for other games, tools.</summary>
    public bool IsPc
    {
        get => _section == Section.Pc;
        set
        {
            if (value)
            {
                SelectPc();
            }
        }
    }

    public bool IsGamePages => _section == Section.Game;

    public string SectionTitle => _section switch
    {
        Section.Gta => "GTA RP",
        Section.Pc => "WINDOWS И ДРУГИЕ ИГРЫ",
        _ => Current.SectionTitle,
    };

    public bool IsDota
    {
        get => IsGamePages && Current == Dota;
        set
        {
            if (value)
            {
                Select(Dota);
            }
        }
    }

    public bool IsCs2
    {
        get => IsGamePages && Current == Cs2;
        set
        {
            if (value)
            {
                Select(Cs2);
            }
        }
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (Set(ref _selectedTab, value))
            {
                ActivatePage(value);
                Live.Active = value == HomeTab;
            }
        }
    }

    public NetworkViewModel Network { get; }
    public SystemTweaksViewModel SystemTweaks { get; }
    public GameModeViewModel GameMode { get; }
    public StartupViewModel Startup { get; }
    public BoostViewModel Boost { get; }
    public PresetsViewModel Presets { get; }
    public BenchmarkViewModel Benchmark { get; }
    public RollbackViewModel Rollback { get; }
    public AboutViewModel About { get; }
    public SettingsViewModel Settings { get; }

    private void Select(GameWorkspace ws)
    {
        var other = ws == Dota ? Cs2 : Dota;
        other.Wizard.IsActive = false;
        Gta.IsActive = false;
        // Theme first: converters read the accent brushes while the new page's bindings are evaluated.
        GameTheme.Apply(ws.Game.Id);
        Current = ws;
        _section = Section.Game;
        if (SelectedTab is >= GtaModesTab and <= BenchmarkTab)
        {
            SelectedTab = OptimizeTab;
        }

        UiState.SaveLastGame(ws.Game.Id);
        RaiseSection();
        _ = ws.Wizard.ActivateAsync();
    }

    private void SelectGta()
    {
        Dota.Wizard.IsActive = false;
        Cs2.Wizard.IsActive = false;
        GameTheme.ApplyGta();
        _section = Section.Gta;
        if (SelectedTab is (>= OptimizeTab and <= CrashTab) or (>= SystemTweaksTab and <= BenchmarkTab))
        {
            SelectedTab = GtaModesTab;
        }

        RaiseSection();
        _ = Gta.ActivateAsync();
    }

    private void SelectPc()
    {
        Dota.Wizard.IsActive = false;
        Cs2.Wizard.IsActive = false;
        Gta.IsActive = false;
        GameTheme.ApplyPc();
        _section = Section.Pc;
        if (SelectedTab is >= OptimizeTab and <= GtaDiagnosisTab)
        {
            SelectedTab = SystemTweaksTab;
        }
        else
        {
            ActivatePage(SelectedTab);
        }

        RaiseSection();
    }

    /// <summary>Re-applies the current section's colours (performance mode removes or brings back the glow).</summary>
    private void ReapplyTheme()
    {
        switch (_section)
        {
            case Section.Gta:
                GameTheme.ApplyGta();
                break;
            case Section.Pc:
                GameTheme.ApplyPc();
                break;
            default:
                GameTheme.Apply(Current.Game.Id);
                break;
        }
    }

    /// <summary>Pages of the Windows section load on first open, so start-up stays fast.</summary>
    private void ActivatePage(int tab)
    {
        _ = tab switch
        {
            SystemTweaksTab => SystemTweaks.ActivateAsync(),
            GameModeTab => GameMode.ActivateAsync(),
            StartupTab => Startup.ActivateAsync(),
            PresetsTab => Presets.ActivateAsync(),
            BenchmarkTab => Benchmark.ActivateAsync(),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>Puts back what the real-time game mode paused, before the window closes.</summary>
    public void Shutdown()
    {
        Live.Dispose();
        GameMode.Shutdown();
        Home.Shutdown();
    }

    private void RaiseSection()
    {
        OnPropertyChanged(nameof(IsGta));
        OnPropertyChanged(nameof(IsPc));
        OnPropertyChanged(nameof(IsGamePages));
        OnPropertyChanged(nameof(IsDota));
        OnPropertyChanged(nameof(IsCs2));
        OnPropertyChanged(nameof(SectionTitle));
    }
}
