using System.Windows;
using System.Windows.Media;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>Everything shown for one game: its wizard, issues, match monitor and crash pages.</summary>
public sealed class GameWorkspace
{
    public GameWorkspace(AppServices services, GameDefinition game, Action<int> navigate)
    {
        Game = game;
        Wizard = new WizardViewModel(services, game);
        Issues = new KnownIssuesViewModel(services, game, navigate);
        Monitor = new MatchMonitorViewModel(services, game);
        Crash = new CrashViewModel(services, game);
    }

    public GameDefinition Game { get; }
    public string Name => Game.Name;
    public string ShortName => Game.ShortName;

    /// <summary>Header of this game's group in the side navigation.</summary>
    public string SectionTitle => Game.Name.ToUpperInvariant();

    /// <summary>Two- or three-letter mark on the game tile.</summary>
    public string Monogram => Game.Id == GameId.Cs2 ? "CS2" : "D2";

    public string Genre => Game.Id == GameId.Cs2 ? "Тактический шутер" : "MOBA";

    public WizardViewModel Wizard { get; }
    public KnownIssuesViewModel Issues { get; }
    public MatchMonitorViewModel Monitor { get; }
    public CrashViewModel Crash { get; }
}

/// <summary>
/// Design B has one look for every section: ink for buttons and selection, flat fills, no glow. The per-section colours of 1.4–1.5
/// (red Dota 2, amber CS2, teal «ПК», violet GTA RP) are gone; the methods stay so the section switch keeps one call site.
/// </summary>
public static class GameTheme
{
    private static readonly Color Ink = Color.FromRgb(0x14, 0x14, 0x14);
    private static readonly Color Paper = Color.FromRgb(0xF3, 0xF0, 0xE8);

    public static void Apply(GameId game) => Apply();

    public static void ApplyPc() => Apply();

    public static void ApplyGta() => Apply();

    private static void Apply()
    {
        var r = Application.Current.Resources;
        r["AccentColor"] = Ink;
        r["Accent"] = Frozen(new SolidColorBrush(Ink));
        r["AccentHover"] = Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)));
        r["AccentPressed"] = Frozen(new SolidColorBrush(Colors.Black));
        r["OnAccent"] = Frozen(new SolidColorBrush(Paper));
        r["AccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb(0x14, Ink.R, Ink.G, Ink.B)));
        r["AccentSoftBorder"] = Frozen(new SolidColorBrush(Ink));
        r["AccentGradient"] = Frozen(new SolidColorBrush(Ink));
        r["BarGradient"] = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x38, 0x0D)));
        r["HeroGradient"] = Brushes.Transparent;
        r["HeroBorder"] = Frozen(new SolidColorBrush(Ink));
        r["Glow.Button"] = null;
        r["Glow"] = Brushes.Transparent;
    }

    private static T Frozen<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

/// <summary>Remembers the last opened Steam game (Dota 2 or CS2) between launches. A plain text file in the app's data folder.</summary>
internal static class UiState
{
    private static string File => Path.Combine(AppPaths.Root, "last-game.txt");

    public static GameId LastGame()
    {
        try
        {
            return System.IO.File.Exists(File) && Enum.TryParse<GameId>(System.IO.File.ReadAllText(File).Trim(), out var g) ? g : GameId.Dota2;
        }
        catch (IOException)
        {
            return GameId.Dota2;
        }
    }

    public static void SaveLastGame(GameId game) => Save(game.ToString());

    private static void Save(string section)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            System.IO.File.WriteAllText(File, section);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience.
        }
    }
}
