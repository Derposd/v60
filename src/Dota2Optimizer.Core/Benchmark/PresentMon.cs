using System.Diagnostics;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Benchmark;

/// <summary>
/// Locates Intel PresentMon (MIT license). The installer puts it into tools\ next to the app; the single-file
/// build also carries an embedded copy that is extracted into the user's own data folder on first use.
/// PresentMon reads ETW events from the OS — it does not touch the game process, so it is VAC-safe.
/// </summary>
public static class PresentMonTool
{
    public const string ResourceName = "PresentMon.exe";

    public static string? Find()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tools");
        if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "PresentMon*.exe").OrderByDescending(f => f).FirstOrDefault() is { } bundled)
        {
            return bundled;
        }

        return ExtractEmbedded();
    }

    private static string? ExtractEmbedded()
    {
        using var stream = typeof(PresentMonTool).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        // %LOCALAPPDATA% is private to the user, so nobody else can swap the exe that later runs elevated.
        var target = Path.Combine(AppPaths.Root, "tools", "PresentMon.exe");
        try
        {
            if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
            {
                return target;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var file = File.Create(target))
            {
                stream.CopyTo(file);
            }

            Log.Info($"PresentMon распакован: {target}");
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось распаковать PresentMon: {ex.Message}");
            return File.Exists(target) ? target : null;
        }
    }

    /// <summary>
    /// Folder for capture files. %ProgramData% has no user name in the path (user names are often Cyrillic),
    /// is writable by the elevated PresentMon and readable by the app itself.
    /// </summary>
    public static string CaptureFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Dota2Optimizer", "fps");
}

/// <summary>
/// One PresentMon recording of a game process. Runs PresentMon directly when the app is already elevated,
/// otherwise through a single UAC prompt; its console output goes to a log next to the CSV so a failure
/// can be explained instead of silently producing no FPS.
/// </summary>
public sealed class FpsCapture
{
    private readonly object _logGate = new();
    private Process? _process;

    private FpsCapture(string csvPath, string logPath, string exeName)
    {
        ExeName = exeName;
        CsvPath = csvPath;
        LogPath = logPath;
    }

    /// <summary>Recorded process, e.g. "cs2.exe" (also the process name inside the CSV).</summary>
    public string ExeName { get; }

    public string CsvPath { get; }
    public string LogPath { get; }

    /// <summary>The user declined the UAC prompt.</summary>
    public bool Declined { get; private set; }

    /// <summary>PresentMon could not be started at all.</summary>
    public string? StartError { get; private set; }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process is null || _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return _process is { HasExited: true } p ? p.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>Starts a whole-match capture that ends by itself when the game closes (or after 90 minutes).</summary>
    public static FpsCapture Start(string presentMon, string exeName = "dota2.exe")
    {
        var dir = PresentMonTool.CaptureFolder;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var capture = new FpsCapture(Path.Combine(dir, $"match-{stamp}.csv"), Path.Combine(dir, $"match-{stamp}.log"), exeName);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            capture.StartError = $"не удалось создать папку {dir}: {ex.Message}";
            return capture;
        }

        var args = $"--process_name {exeName} --output_file \"{capture.CsvPath}\" --timed 5400 --terminate_after_timed " +
                   "--terminate_on_proc_exit --stop_existing_session --session_name D2OMatch --no_console_stats";
        Log.Info($"Запуск PresentMon: {presentMon} {args}");
        try
        {
            capture._process = Elevation.IsElevated ? capture.StartDirect(presentMon, args) : capture.StartElevated(presentMon, args);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            capture.Declined = true;
            Log.Warn("Запрос прав администратора для PresentMon отклонён");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            capture.StartError = ex.Message;
            Log.Warn($"PresentMon не запустился: {ex.Message}");
        }

        return capture;
    }

    private Process? StartDirect(string presentMon, string args)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(presentMon, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        p.OutputDataReceived += (_, e) => AppendLog(e.Data);
        p.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private Process? StartElevated(string presentMon, string args)
    {
        // cmd.exe redirects PresentMon's console output into the log; one UAC prompt covers both.
        var cmd = $"/d /s /c \"\"{presentMon}\" {args} > \"{LogPath}\" 2>&1\"";
        return Process.Start(new ProcessStartInfo("cmd.exe", cmd)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_logGate)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // The log is only for diagnostics.
            }
        }
    }

    /// <summary>Last meaningful line PresentMon printed (usually the error).</summary>
    public string? LastLogLine()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return null;
            }

            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd().Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Reads the CSV even while PresentMon is still writing it.</summary>
    public string? ReadCsv()
    {
        try
        {
            if (!File.Exists(CsvPath))
            {
                return null;
            }

            using var fs = new FileStream(CsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            Log.Warn($"Не удалось прочитать запись FPS: {ex.Message}");
            return null;
        }
    }

    /// <summary>Explains why no FPS came out of this capture, or null if the capture is fine.</summary>
    public string? Problem(int framesRecorded)
    {
        if (framesRecorded > 0)
        {
            return null;
        }

        if (Declined)
        {
            return "вы отклонили запрос прав администратора — без них Windows не даёт записывать кадры";
        }

        if (StartError is not null)
        {
            return "PresentMon не запустился: " + StartError;
        }

        var last = LastLogLine();
        if (HasExited && ExitCode is { } code && code != 0)
        {
            return $"PresentMon завершился с ошибкой (код {code})" + (last is null ? string.Empty : ": " + last);
        }

        if (!File.Exists(CsvPath))
        {
            return "PresentMon не создал файл записи" + (last is null ? string.Empty : ": " + last);
        }

        return $"PresentMon работал, но не увидел кадров {ExeName}. Начинайте запись, когда игра уже открыта, " +
               "и не закрывайте её до конца матча; если используете оверлей записи экрана, попробуйте без него";
    }
}
