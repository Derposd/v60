using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Benchmark;

/// <summary>
/// Samples system-wide performance counters while Dota 2 runs: CPU clock (as % of base), CPU utility,
/// free RAM, the game's memory and VRAM (from the "GPU Process Memory" counters) and 3D load.
/// Process memory figures come from the system process list; no handle to dota2.exe is opened.
/// </summary>
public sealed partial class MatchMonitor : IDisposable
{
    private readonly PerformanceCounter? _cpuPerf;
    private readonly PerformanceCounter? _cpuUtil;
    private readonly PerformanceCounter? _ramAvail;
    private readonly Dictionary<string, PerformanceCounter> _gpuEngine = new();
    private readonly Dictionary<string, PerformanceCounter> _gpuMem = new();
    private readonly DateTime _start = DateTime.UtcNow;

    public MatchMonitor()
    {
        _cpuPerf = TryCounter("Processor Information", "% Processor Performance", "_Total");
        _cpuUtil = TryCounter("Processor Information", "% Processor Utility", "_Total") ?? TryCounter("Processor", "% Processor Time", "_Total");
        _ramAvail = TryCounter("Memory", "Available MBytes", null);
    }

    [GeneratedRegex(@"^pid_(\d+)_", RegexOptions.IgnoreCase)]
    private static partial Regex PidPrefix();

    public List<MatchSample> Samples { get; } = new();

    private static PerformanceCounter? TryCounter(string category, string counter, string? instance)
    {
        try
        {
            if (!PerformanceCounterCategory.Exists(category))
            {
                return null;
            }

            var c = instance is null ? new PerformanceCounter(category, counter, readOnly: true) : new PerformanceCounter(category, counter, instance, readOnly: true);
            c.NextValue();
            return c;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Счётчик {category}\\{counter} недоступен: {ex.Message}");
            return null;
        }
    }

    /// <summary>Takes one sample. Returns null when Dota 2 isn't running.</summary>
    public MatchSample? Sample()
    {
        var procs = Process.GetProcessesByName("dota2");
        try
        {
            if (procs.Length == 0)
            {
                return null;
            }

            var pids = procs.Select(p => p.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
            double ws = 0, priv = 0;
            foreach (var p in procs)
            {
                try
                {
                    ws += p.WorkingSet64 / 1048576.0;
                    priv += p.PrivateMemorySize64 / 1048576.0;
                }
                catch (InvalidOperationException)
                {
                    // Exited between enumeration and read.
                }
            }

            var sample = new MatchSample(
                (DateTime.UtcNow - _start).TotalSeconds,
                ws,
                priv,
                Read(_ramAvail),
                Read(_cpuPerf),
                Read(_cpuUtil),
                SumForPids("GPU Engine", "Utilization Percentage", _gpuEngine, pids, name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)),
                SumForPids("GPU Process Memory", "Dedicated Usage", _gpuMem, pids, _ => true) / 1048576.0);
            Samples.Add(sample);
            return sample;
        }
        finally
        {
            foreach (var p in procs)
            {
                p.Dispose();
            }
        }
    }

    private static double Read(PerformanceCounter? c)
    {
        try
        {
            return c?.NextValue() ?? 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static double SumForPids(string category, string counter, Dictionary<string, PerformanceCounter> cache, HashSet<string> pids, Func<string, bool> filter)
    {
        try
        {
            if (!PerformanceCounterCategory.Exists(category))
            {
                return 0;
            }

            var names = new PerformanceCounterCategory(category).GetInstanceNames()
                .Where(n => PidPrefix().Match(n) is { Success: true } m && pids.Contains(m.Groups[1].Value) && filter(n))
                .ToHashSet();
            foreach (var stale in cache.Keys.Where(k => !names.Contains(k)).ToList())
            {
                cache[stale].Dispose();
                cache.Remove(stale);
            }

            double sum = 0;
            foreach (var n in names)
            {
                if (!cache.TryGetValue(n, out var c))
                {
                    c = new PerformanceCounter(category, counter, n, readOnly: true);
                    c.NextValue();
                    cache[n] = c;
                }

                sum += c.NextValue();
            }

            return sum;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    /// <summary>Starts a long PresentMon capture that ends by itself when Dota 2 closes (or after 90 minutes).</summary>
    public static Task<int> StartFpsCaptureAsync(string presentMon, string csvPath) =>
        Task.Run(() => ElevatedProcess.RunAs(presentMon,
            $"--process_name dota2.exe --output_file \"{csvPath}\" --timed 5400 --terminate_after_timed --terminate_on_proc_exit " +
            "--stop_existing_session --session_name D2OMatch --no_console_stats",
            TimeSpan.FromMinutes(95)));

    public void Dispose()
    {
        _cpuPerf?.Dispose();
        _cpuUtil?.Dispose();
        _ramAvail?.Dispose();
        foreach (var c in _gpuEngine.Values.Concat(_gpuMem.Values))
        {
            c.Dispose();
        }
    }
}
