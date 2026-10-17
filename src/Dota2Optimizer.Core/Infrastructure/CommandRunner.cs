using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace Dota2Optimizer.Core.Infrastructure;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public interface ICommandRunner
{
    CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null)
    {
        Log.Info($"Команда: {fileName} {arguments}");
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Console tools (powercfg, netsh) print in the OEM code page.
            StandardOutputEncoding = ConsoleEncoding(),
            StandardErrorEncoding = ConsoleEncoding(),
        };
        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Tool missing or blocked: report as a failed command instead of breaking the caller.
            Log.Warn($"Не удалось запустить {fileName}: {ex.Message}");
            return new CommandResult(-1, string.Empty, ex.Message);
        }

        using var p = started ?? throw new InvalidOperationException($"Не удалось запустить {fileName}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            return new CommandResult(-1, stdout.Result, "Таймаут выполнения");
        }

        var result = new CommandResult(p.ExitCode, stdout.Result, stderr.Result);
        if (!result.Success)
        {
            Log.Warn($"Команда завершилась с кодом {p.ExitCode}: {result.StdErr.Trim()} {result.StdOut.Trim()}");
        }

        return result;
    }

    private static Encoding ConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}

public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

/// <summary>Starts a process through the UAC prompt and waits for it. Used only for single, user-confirmed admin commands.</summary>
public static class ElevatedProcess
{
    /// <returns>Exit code, or -1 if the user declined the UAC prompt.</returns>
    public static int RunAs(string fileName, string arguments, TimeSpan? timeout = null)
    {
        Log.Info($"Запуск с правами администратора: {fileName} {arguments}");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null)
            {
                return -1;
            }

            p.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(5)).TotalMilliseconds);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Warn("Пользователь отклонил запрос UAC");
            return -1;
        }
    }
}
