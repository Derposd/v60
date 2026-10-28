using System.Text;

namespace Dota2Optimizer.Core.Infrastructure;

public static class AppPaths
{
    private static string? _rootOverride;

    /// <summary>%LOCALAPPDATA%\Dota2Optimizer. Overridable for tests and for the elevated helper.</summary>
    public static string Root =>
        _rootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dota2Optimizer");

    public static string Logs => Path.Combine(Root, "logs");
    public static string Backups => Path.Combine(Root, "backups");
    public static string History => Path.Combine(Root, "history.json");
    public static string ScanCache => Path.Combine(Root, "last-scan.json");
    public static string SessionBoost => Path.Combine(Root, "session-power.txt");
    public static string SessionBoostOff => Path.Combine(Root, "session-boost-off");
    public static string RestorePointMarker => Path.Combine(Root, "restore-point-created.txt");

    public static void OverrideRoot(string root) => _rootOverride = root;
}

/// <summary>Minimal thread-safe file logger: one file per day, nothing leaves the machine.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static event Action<string>? LineWritten;

    public static string CurrentFile => Path.Combine(AppPaths.Logs, $"d2o-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(CurrentFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never break the app.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        LineWritten?.Invoke(line);
    }
}
