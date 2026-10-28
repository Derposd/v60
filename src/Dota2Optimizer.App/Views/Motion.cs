using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.Views;

/// <summary>Interface settings kept between launches (a small JSON file next to the backups).</summary>
public sealed class AppSettings
{
    /// <summary>«Уменьшить анимации»: transitions become instant.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>«Режим производительности»: no glow, shadows or animated backgrounds at all.</summary>
    public bool PerformanceMode { get; set; }

    private static string File => Path.Combine(AppPaths.Root, "ui-settings.json");

    public static AppSettings Load()
    {
        try
        {
            return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File)) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось сохранить настройки интерфейса: {ex.Message}");
        }
    }
}

/// <summary>
/// One switch for all motion. Code-driven animations (page transitions, card appearance, counters, rings) ask <see cref="Enabled"/>
/// every time, so the setting applies at once; template animations read the duration tokens, which are rewritten at start-up.
/// </summary>
public static class Motion
{
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>Windows «Показывать анимацию в Windows» (Параметры → Специальные возможности → Визуальные эффекты).</summary>
    public static bool SystemAllowsAnimation => SystemParameters.ClientAreaAnimation;

    public static bool Enabled => !Settings.ReduceMotion && !Settings.PerformanceMode && SystemAllowsAnimation;

    public static bool Performance => Settings.PerformanceMode;

    public static TimeSpan Fast => Enabled ? TimeSpan.FromMilliseconds(140) : TimeSpan.Zero;
    public static TimeSpan Base => Enabled ? TimeSpan.FromMilliseconds(200) : TimeSpan.Zero;
    public static TimeSpan Slow => Enabled ? TimeSpan.FromMilliseconds(320) : TimeSpan.Zero;
    public static TimeSpan Stagger => Enabled ? TimeSpan.FromMilliseconds(35) : TimeSpan.Zero;

    public static event Action? Changed;

    /// <summary>Call once at start-up, before the main window is created, and again when the user changes a setting.</summary>
    public static void Apply(AppSettings settings)
    {
        Settings = settings;
        var r = Application.Current.Resources;
        if (!Enabled)
        {
            foreach (var key in new[] { "Dur.Instant", "Dur.Fast", "Dur.Base", "Dur.Slow" })
            {
                r[key] = new Duration(TimeSpan.Zero);
            }
        }
        else
        {
            r["Dur.Instant"] = new Duration(TimeSpan.FromMilliseconds(80));
            r["Dur.Fast"] = new Duration(TimeSpan.FromMilliseconds(140));
            r["Dur.Base"] = new Duration(TimeSpan.FromMilliseconds(200));
            r["Dur.Slow"] = new Duration(TimeSpan.FromMilliseconds(320));
        }

        // Performance mode: the accent glow and soft shadows cost GPU fill rate on every frame; drop them.
        if (Performance)
        {
            r["Glow"] = Brushes.Transparent;
            r["Shadow.Float"] = null;
        }

        Changed?.Invoke();
    }

    public static void Save(AppSettings settings)
    {
        settings.Save();
        Apply(settings);
    }
}
