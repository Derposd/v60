using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.SystemInfo;

/// <summary>
/// Finds which adapter a game renders on using the system-wide "GPU Engine" performance counters
/// (the same data Task Manager shows). No handle to the game process is opened, nothing is read from it.
/// </summary>
public static partial class GpuUsageProbe
{
    [GeneratedRegex(@"pid_(\d+)_luid_(0x[0-9A-F]{8}_0x[0-9A-F]{8})_.*engtype_3D", RegexOptions.IgnoreCase)]
    private static partial Regex InstanceName();

    public static string? FindGameGpu(IReadOnlyList<GpuInfo> gpus, string processName)
    {
        var pids = Process.GetProcessesByName(processName).Select(p =>
        {
            using (p)
            {
                return p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }).ToHashSet();
        if (pids.Count == 0 || !PerformanceCounterCategory.Exists("GPU Engine"))
        {
            return null;
        }

        var category = new PerformanceCounterCategory("GPU Engine");
        var counters = new List<(string Luid, PerformanceCounter Counter)>();
        foreach (var name in category.GetInstanceNames())
        {
            var m = InstanceName().Match(name);
            if (m.Success && pids.Contains(m.Groups[1].Value))
            {
                counters.Add((m.Groups[2].Value, new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true)));
            }
        }

        try
        {
            // Rate counters need two samples.
            foreach (var c in counters)
            {
                c.Counter.NextValue();
            }

            Thread.Sleep(1000);
            var best = counters
                .GroupBy(c => c.Luid, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Luid: g.Key, Load: g.Sum(c => c.Counter.NextValue())))
                .OrderByDescending(x => x.Load)
                .FirstOrDefault();
            if (best.Luid is null)
            {
                return null;
            }

            return gpus.FirstOrDefault(g => string.Equals(g.Luid, best.Luid, StringComparison.OrdinalIgnoreCase))?.Name ?? $"адаптер {best.Luid}";
        }
        finally
        {
            foreach (var c in counters)
            {
                c.Counter.Dispose();
            }
        }
    }
}
