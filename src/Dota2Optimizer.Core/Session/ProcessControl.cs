using System.Diagnostics;
using System.Management;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Session;

public sealed record RunningProcess(int Pid, string Name, string? Path, string? CommandLine);

public interface IProcessControl
{
    /// <summary>Names (without .exe) of running processes. Reads the process list only; no handles are opened.</summary>
    IReadOnlyList<string> RunningNames();

    /// <summary>Full details (path, command line) of processes with this name, for a later restart.</summary>
    IReadOnlyList<RunningProcess> Find(string name);

    /// <summary>Asks the program to close (main window), then ends it if it doesn't within the timeout.</summary>
    bool Close(int pid, TimeSpan timeout);

    bool Start(string path, string? arguments);

    /// <summary>Sets High priority, like Task Manager does. Returns false if the process refused (e.g. protected by an anti-cheat).</summary>
    bool SetHighPriority(string name);

    bool SetNormalPriority(string name);
}

public sealed class ProcessControl : IProcessControl
{
    public IReadOnlyList<string> RunningNames()
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                names.Add(p.ProcessName);
            }
        }

        return names;
    }

    public IReadOnlyList<RunningProcess> Find(string name)
    {
        var result = new List<RunningProcess>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = '{name.Replace("'", string.Empty)}.exe'");
            foreach (var o in searcher.Get())
            {
                using (o)
                {
                    result.Add(new RunningProcess(Convert.ToInt32(o["ProcessId"]), name, o["ExecutablePath"] as string, o["CommandLine"] as string));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            Log.Warn($"WMI недоступен для {name}: {ex.Message}");
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    string? path = null;
                    try
                    {
                        path = p.MainModule?.FileName;
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                    }

                    result.Add(new RunningProcess(p.Id, name, path, null));
                }
            }
        }

        return result;
    }

    public bool Close(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.CloseMainWindow() && p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                return true;
            }

            p.Kill();
            return p.WaitForExit(3000);
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Не удалось закрыть процесс {pid}: {ex.Message}");
            return false;
        }
    }

    public bool Start(string path, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path, arguments ?? string.Empty) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? string.Empty })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn($"Не удалось запустить {path}: {ex.Message}");
            return false;
        }
    }

    public bool SetHighPriority(string name) => SetPriority(name, ProcessPriorityClass.High);

    public bool SetNormalPriority(string name) => SetPriority(name, ProcessPriorityClass.Normal);

    private static bool SetPriority(string name, ProcessPriorityClass cls)
    {
        var ok = false;
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try
                {
                    p.PriorityClass = cls;
                    ok = true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    Log.Warn($"Приоритет {name}: {ex.Message}");
                }
            }
        }

        return ok;
    }
}
