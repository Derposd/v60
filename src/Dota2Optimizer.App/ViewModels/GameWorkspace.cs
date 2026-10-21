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
/// Each game has its own accent colour (Dota — crimson, CS2 — amber). The brushes are replaced in the
/// application resources, and every accent use in XAML is a DynamicResource, so the whole window recolours.
/// </summary>
public static class GameTheme
{
    public static void Apply(GameId game)
    {
        var (accent, hover, pressed, onAccent) = game == GameId.Cs2
            ? (Color.FromRgb(0xF5, 0xA5, 0x24), Color.FromRgb(0xF8, 0xB9, 0x4D), Color.FromRgb(0xDB, 0x8C, 0x10), Color.FromRgb(0x1B, 0x12, 0x04))
            : (Color.FromRgb(0xF0, 0x45, 0x3A), Color.FromRgb(0xF4, 0x67, 0x5D), Color.FromRgb(0xD3, 0x31, 0x27), Colors.White);

        var r = Application.Current.Resources;
        r["AccentColor"] = accent;
        r["Accent"] = Frozen(new SolidColorBrush(accent));
        r["AccentHover"] = Frozen(new SolidColorBrush(hover));
        r["AccentPressed"] = Frozen(new SolidColorBrush(pressed));
        r["OnAccent"] = Frozen(new SolidColorBrush(onAccent));
        r["AccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)));
        r["AccentSoftBorder"] = Frozen(new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)));
        r["AccentGradient"] = Frozen(new LinearGradientBrush(hover, pressed, new Point(0, 0), new Point(1, 1)));
        r["BarGradient"] = Frozen(new LinearGradientBrush(hover, pressed, new Point(0, 0), new Point(0, 1)));
        r["HeroGradient"] = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0x30, accent.R, accent.G, accent.B), 0),
            new(Color.FromArgb(0x08, accent.R, accent.G, accent.B), 0.55),
            new(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1),
        }, new Point(0, 0), new Point(1, 1)));
        r["HeroBorder"] = Frozen(new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)));
        r["Glow"] = Frozen(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0x2C, accent.R, accent.G, accent.B), 0),
            new(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1),
        }) { Center = new Point(0.85, 0), GradientOrigin = new Point(0.85, 0), RadiusX = 0.7, RadiusY = 1.1 });
    }

    private static T Frozen<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

/// <summary>Remembers the last opened game between launches. A plain text file in the app's data folder.</summary>
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

    public static void SaveLastGame(GameId game)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            System.IO.File.WriteAllText(File, game.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience.
        }
    }
}
