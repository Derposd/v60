using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Input;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Session;
using Dota2Optimizer.Core.Upscale;

namespace Dota2Optimizer.App.ViewModels;

public sealed class AlgorithmVm : ObservableObject
{
    private readonly MagpieViewModel _owner;

    public AlgorithmVm(MagpieViewModel owner, UpscaleAlgorithm a)
    {
        _owner = owner;
        Algorithm = a;
    }

    public UpscaleAlgorithm Algorithm { get; }
    public string Name => Algorithm.Ru();
    public string Description => Algorithm.Describe();

    public bool IsSelected
    {
        get => _owner.Algorithm == Algorithm;
        set
        {
            if (value)
            {
                _owner.Algorithm = Algorithm;
            }
        }
    }

    public void Raise() => OnPropertyChanged(nameof(IsSelected));
}

public sealed record GpuChoice(int Index, uint VendorId, uint DeviceId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Settings FrameLab keeps for its Magpie copy (the Magpie config is rebuilt from these on «Применить»).</summary>
public sealed class MagpieState
{
    public UpscaleAlgorithm Algorithm { get; set; } = UpscaleAlgorithm.Fsr;
    public UpscalePreset Preset { get; set; } = UpscalePreset.Balance;
    public List<MagpieGameProfile> Games { get; set; } = new();
}

/// <summary>B1/B4: the window upscaler through FrameLab's own copy of Magpie — install, algorithm, preset, per-game profile, video card.</summary>
public sealed class MagpieViewModel : ObservableObject
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static string StatePath => Path.Combine(AppPaths.Root, "upscale.json");

    private readonly AppServices _s;
    private readonly MagpieState _state;
    private string? _status;
    private bool _busy;
    private double _progress;
    private GpuChoice? _gpu;
    private bool _autoScale = true;

    public MagpieViewModel(AppServices services)
    {
        _s = services;
        _state = Load();
        AlgorithmChoices = MagpieSetup.Algorithms.Select(a => new AlgorithmVm(this, a)).ToList();
        InstallCommand = new AsyncCommand(InstallAsync, () => !Busy);
        UninstallCommand = new RelayCommand(Uninstall, () => !Busy && Installed);
        StartCommand = new RelayCommand(() => Run(MagpieSetup.Start), () => !Busy && Installed);
        StopCommand = new RelayCommand(() => Run(MagpieSetup.Stop), () => !Busy && Installed);
        ApplyCommand = new RelayCommand(Apply, () => !Busy && Installed);
        AddRunningGameCommand = new RelayCommand(AddRunningGame, () => !Busy && Installed);
        RemoveGameCommand = new RelayCommand(p => RemoveGame(p as MagpieGameProfile));
        OpenProjectCommand = new RelayCommand(() => Ui.OpenUrl(MagpieSetup.ProjectUrl));
        Games = new ObservableCollection<MagpieGameProfile>(_state.Games);
    }

    public IReadOnlyList<AlgorithmVm> AlgorithmChoices { get; }
    public ObservableCollection<GpuChoice> Gpus { get; } = new();
    public ObservableCollection<MagpieGameProfile> Games { get; }
    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand AddRunningGameCommand { get; }
    public ICommand RemoveGameCommand { get; }
    public ICommand OpenProjectCommand { get; }

    public bool Installed => MagpieSetup.Installed;
    public bool Running => Installed && MagpieSetup.Running();
    public bool ManyGpus => Gpus.Count > 2; // "Автоматически" plus at least two cards
    public string Hotkey => MagpieSetup.ScaleHotkey;

    public string InstallText => Installed
        ? $"Magpie {MagpieSetup.InstalledVersion() ?? string.Empty} — копия FrameLab в {MagpieSetup.InstallDir}" + (Running ? " · работает (значок в трее)" : " · не запущен")
        : "Не установлен. Magpie — отдельная бесплатная программа (GPLv3); FrameLab скачает официальный релиз только по кнопке.";

    public UpscaleAlgorithm Algorithm
    {
        get => _state.Algorithm;
        set
        {
            _state.Algorithm = value;
            foreach (var a in AlgorithmChoices)
            {
                a.Raise();
            }

            OnPropertyChanged(nameof(ResolutionText));
            Save();
        }
    }

    public bool IsQuality
    {
        get => _state.Preset == UpscalePreset.Quality;
        set => SetPreset(value, UpscalePreset.Quality);
    }

    public bool IsBalance
    {
        get => _state.Preset == UpscalePreset.Balance;
        set => SetPreset(value, UpscalePreset.Balance);
    }

    public bool IsPerformance
    {
        get => _state.Preset == UpscalePreset.Performance;
        set => SetPreset(value, UpscalePreset.Performance);
    }

    public GpuChoice? Gpu
    {
        get => _gpu;
        set => Set(ref _gpu, value);
    }

    public bool AutoScale
    {
        get => _autoScale;
        set => Set(ref _autoScale, value);
    }

    /// <summary>What resolution to set in the game for the chosen preset, on this monitor.</summary>
    public string ResolutionText
    {
        get
        {
            var d = _s.Snapshot?.Display;
            if (d is null || d.Width <= 0)
            {
                return "Поставьте в игре режим «В окне» или «Окно без рамки» и разрешение ниже экрана.";
            }

            var (w, h) = MagpieSetup.GameResolution(d.Width, d.Height, _state.Preset, _state.Algorithm);
            var pct = _state.Algorithm == UpscaleAlgorithm.Integer ? 50 : (int)Math.Round(MagpieSetup.RenderScale(_state.Preset) * 100);
            return $"В игре: режим «В окне» (не «Полный экран»), разрешение около {w}×{h} — {pct}% от экрана {d.Width}×{d.Height}. Потом нажмите {Hotkey}.";
        }
    }

    public string? Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            Set(ref _busy, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public void SetGpus(IReadOnlyList<GpuCaps> caps)
    {
        Gpus.Clear();
        Gpus.Add(new GpuChoice(-1, 0, 0, "Автоматически (как решит Magpie)"));
        foreach (var c in caps.Where(c => c.Index >= 0))
        {
            Gpus.Add(new GpuChoice(c.Index, c.VendorId, c.DeviceId, c.Name));
        }

        Gpu = Gpus[0];
        OnPropertyChanged(nameof(ManyGpus));
        OnPropertyChanged(nameof(ResolutionText));
        RaiseInstall();
    }

    private void SetPreset(bool on, UpscalePreset p)
    {
        if (!on)
        {
            return;
        }

        _state.Preset = p;
        OnPropertyChanged(nameof(IsQuality));
        OnPropertyChanged(nameof(IsBalance));
        OnPropertyChanged(nameof(IsPerformance));
        OnPropertyChanged(nameof(ResolutionText));
        Save();
    }

    private async Task InstallAsync()
    {
        if (!Ui.Confirm(
                "Magpie — отдельная бесплатная программа с открытым кодом (лицензия GNU GPL v3, автор Blinue). Она не встроена в FrameLab.\n\n" +
                "FrameLab скачает официальный релиз с GitHub (" + MagpieSetup.ProjectUrl + "), проверит контрольную сумму и поставит отдельную копию в свою папку " +
                "(уже установленный у вас Magpie не трогается). Исходный код и лицензия — на странице проекта.\n\nСкачать?", "Magpie"))
        {
            return;
        }

        Busy = true;
        Progress = 0;
        try
        {
            Status = "Узнаю последнюю версию…";
            var release = await MagpieSetup.LatestAsync(Http, CancellationToken.None);
            Status = $"Скачиваю Magpie {release.Version}…";
            await MagpieSetup.InstallAsync(Http, release, new Progress<double>(p => Progress = p), CancellationToken.None);
            Apply();
            Status = $"Magpie {release.Version} установлен и запущен. {ResolutionText}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Status = "Не получилось: " + ex.Message;
        }
        finally
        {
            Busy = false;
            RaiseInstall();
        }
    }

    private void Uninstall()
    {
        if (!Ui.Confirm("Удалить копию Magpie из папки FrameLab вместе с её настройками? Ваш собственный Magpie (если есть) не затрагивается.", "Magpie"))
        {
            return;
        }

        Run(MagpieSetup.Uninstall);
        Status = "Копия Magpie удалена.";
    }

    /// <summary>Rebuilds Magpie's config from FrameLab's settings. Magpie is closed first (it rewrites its config on exit) and started again.</summary>
    private void Apply()
    {
        try
        {
            var wasRunning = MagpieSetup.Running();
            MagpieSetup.Stop();
            MagpieSetup.WriteConfig(MagpieSetup.ConfigPath, _state.Algorithm, _state.Preset, _state.Games);
            MagpieSetup.Start();
            Status = (wasRunning ? "Настройки применены, Magpie перезапущен. " : "Настройки применены, Magpie запущен в трее. ") + ResolutionText;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = "Не удалось записать настройки Magpie: " + ex.Message;
        }

        RaiseInstall();
    }

    /// <summary>Remembers the currently running game (from the game mode list) with its window class, so Magpie scales it by itself.</summary>
    private void AddRunningGame()
    {
        var config = GameModeConfigStore.Load(_s.Fs, AppPaths.GameModeConfig);
        var running = new ProcessControl().RunningNames();
        var game = GameSessionManager.RunningGame(new GameModeConfig { Enabled = true, Games = config.Games }, running);
        if (game is null)
        {
            Status = "Запустите игру в окне и нажмите ещё раз. Игры, которых нет в списке, добавьте на странице «Игровой режим».";
            return;
        }

        var cls = GameWindows.ClassOf(game.Process);
        var path = game.Path ?? new ProcessControl().Find(game.Process).FirstOrDefault()?.Path;
        if (cls is null || path is null)
        {
            Status = $"Окно {game.Name} не найдено — разверните игру и попробуйте ещё раз.";
            return;
        }

        var g = Gpu ?? new GpuChoice(-1, 0, 0, string.Empty);
        var profile = new MagpieGameProfile(path, cls, game.Name, _state.Algorithm, _state.Preset, g.Index, g.VendorId, g.DeviceId, AutoScale);
        _state.Games.RemoveAll(x => x.ExePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        _state.Games.Add(profile);
        var old = Games.FirstOrDefault(x => x.ExePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (old is not null)
        {
            Games.Remove(old);
        }

        Games.Add(profile);
        Save();
        Apply();
        Status = $"{game.Name}: {profile.Algorithm.Ru()}, " + (AutoScale ? "масштабирование включится само при запуске игры. " : $"масштабирование — по {Hotkey}. ") + Status;
    }

    private void RemoveGame(MagpieGameProfile? p)
    {
        if (p is null)
        {
            return;
        }

        _state.Games.Remove(p);
        Games.Remove(p);
        Save();
        if (Installed)
        {
            Apply();
        }
    }

    private void Run(Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = ex.Message;
        }

        RaiseInstall();
    }

    private void RaiseInstall()
    {
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(InstallText));
        CommandManager.InvalidateRequerySuggested();
    }

    private static MagpieState Load()
    {
        try
        {
            return File.Exists(StatePath) ? JsonSerializer.Deserialize<MagpieState>(File.ReadAllText(StatePath)) ?? new MagpieState() : new MagpieState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new MagpieState();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось сохранить настройки апскейла: {ex.Message}");
        }
    }
}
