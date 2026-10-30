using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.Core.GamePresets;

namespace Dota2Optimizer.App.ViewModels;

public sealed class PresetGameVm
{
    public PresetGameVm(GameConfigDef game, IEnumerable<TweakRowVm> rows)
    {
        Game = game;
        Rows = new ObservableCollection<TweakRowVm>(rows);
    }

    public GameConfigDef Game { get; }
    public string Name => Game.Name;
    public string Caveat => Game.Caveat;
    public ObservableCollection<TweakRowVm> Rows { get; }
    public bool Found => Rows.Any(r => !r.IsUnsupported);
    public string FoundText => Found ? "Файл настроек найден" : "Игра не найдена или ещё ни разу не запускалась";
}

/// <summary>«Пресеты игр»: config-file presets for popular games, each one a switch with backup and undo.</summary>
public sealed class PresetsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private bool _loading;
    private string? _status;

    public PresetsViewModel(AppServices services)
    {
        _s = services;
        var roots = ConfigRoots.Current();
        Games = new ObservableCollection<PresetGameVm>(GamePresetCatalog.All.Select(g =>
            new PresetGameVm(g, g.Presets.Select(p => new TweakRowVm(new GamePresetTweak(g, p, roots), ToggleAsync)))));
        RefreshCommand = new AsyncCommand(LoadAsync, () => !IsLoading);
    }

    public ObservableCollection<PresetGameVm> Games { get; }
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

    public Task ActivateAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await TweakToggler.EvaluateAsync(_s, Games.SelectMany(g => g.Rows).ToList());
            var games = Games.ToList();
            Games.Clear();
            foreach (var g in games.OrderByDescending(g => g.Found))
            {
                Games.Add(g);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ToggleAsync(TweakRowVm row)
    {
        // Applying a second preset of the same game while another is on would stack backups; switch the other one off first.
        var other = Games.First(g => g.Rows.Contains(row)).Rows.FirstOrDefault(r => r != row && r.State?.CanTurnOff == true);
        if (other is not null && row.State?.CanTurnOn == true)
        {
            Ui.Info($"Сначала выключите «{other.Title}» — для одной игры действует один пресет.");
            return;
        }

        Status = await TweakToggler.ToggleAsync(_s, row) ?? Status;
    }
}
