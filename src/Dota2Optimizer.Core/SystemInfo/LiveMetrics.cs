using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.SystemInfo;

public sealed record LiveSample(double? CpuPercent, double? GpuPercent, double? RamPercent, double RamUsedGb, double RamTotalGb, double? TempC);

/// <summary>
/// Live load for the dashboard: system-wide performance counters (CPU, GPU engines like Task Manager), memory status and the ACPI
/// thermal zone. Nothing is read from any game. Sampling is cheap; the caller decides how often (only while the page is visible).
/// </summary>
public sealed class LiveMetrics : IDisposable
{
    private readonly PerformanceCounter? _cpu;
    private PerformanceCounterCategory? _gpuCategory;
    private readonly Dictionary<string, PerformanceCounter> _gpu = new();
    private DateTime _gpuRefreshed = DateTime.MinValue;
    private bool _tempUnavailable;

    public LiveMetrics()
    {
        _cpu = TryCounter("Processor Information", "% Processor Utility", "_Total") ?? TryCounter("Processor", "% Processor Time", "_Total");
        _cpu?.NextValue();
    }

    private bool _logged;

    public LiveSample Sample()
    {
        var sw = Stopwatch.StartNew();
        double? cpu = null;
        try
        {
            cpu = _cpu is null ? null : Math.Clamp(_cpu.NextValue(), 0, 100);
        }
        catch (InvalidOperationException)
        {
        }

        var (used, total) = Memory();
        var gpu = Gpu();
        var temp = Temperature();
        if (!_logged)
        {
            _logged = true;
            Log.Info($"Живые показатели: первый замер {sw.ElapsedMilliseconds} мс (CPU {cpu?.ToString("0") ?? "нет"}, GPU {gpu?.ToString("0") ?? "нет"}, ОЗУ {total:0.0} ГБ, t {temp?.ToString("0") ?? "нет"})");
        }

        return new LiveSample(cpu, gpu, total > 0 ? used / total * 100 : null, used, total, temp);
    }

    /// <summary>Sum of 3D engine utilisation per adapter, highest adapter wins (what Task Manager shows as "GPU").</summary>
    private double? Gpu()
    {
        try
        {
            if (DateTime.UtcNow - _gpuRefreshed > TimeSpan.FromSeconds(10))
            {
                // Engine instances come and go with processes; refresh the list now and then.
                _gpuRefreshed = DateTime.UtcNow;
                _gpuCategory ??= PerformanceCounterCategory.Exists("GPU Engine") ? new PerformanceCounterCategory("GPU Engine") : null;
                if (_gpuCategory is null)
                {
                    return null;
                }

                var names = _gpuCategory.GetInstanceNames().Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
                foreach (var gone in _gpu.Keys.Where(k => !names.Contains(k)).ToList())
                {
                    _gpu[gone].Dispose();
                    _gpu.Remove(gone);
                }

                foreach (var n in names.Where(n => !_gpu.ContainsKey(n)))
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, readOnly: true);
                    c.NextValue();
                    _gpu[n] = c;
                }
            }

            if (_gpu.Count == 0)
            {
                return null;
            }

            return Math.Clamp(_gpu.GroupBy(kv => Luid(kv.Key)).Max(g => g.Sum(kv => Safe(kv.Value))), 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string Luid(string instance)
    {
        var i = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? instance : instance.Substring(i, Math.Min(27, instance.Length - i));
    }

    private static float Safe(PerformanceCounter c)
    {
        try
        {
            return c.NextValue();
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>ACPI thermal zone (often the CPU or motherboard). Many desktops don't expose it — then null.</summary>
    private double? Temperature()
    {
        if (_tempUnavailable)
        {
            return null;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\CIMV2"),
                new ObjectQuery("SELECT HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = true, Rewindable = false });
            double? best = null;
            foreach (var o in searcher.Get())
            {
                using (o)
                {
                    var raw = Convert.ToDouble(o["HighPrecisionTemperature"] ?? 0) / 10.0;
                    if (raw <= 0)
                    {
                        raw = Convert.ToDouble(o["Temperature"] ?? 0);
                    }

                    var c = raw - 273.15;
                    if (c is > 5 and < 125)
                    {
                        best = Math.Max(best ?? c, c);
                    }
                }
            }

            _tempUnavailable = best is null;
            return best;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidCastException)
        {
            _tempUnavailable = true;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>Physical memory in use and total, GB. A single cheap call; safe on the UI thread.</summary>
    public static (double UsedGb, double TotalGb) Memory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (0, 0);
        }

        var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref s))
        {
            return (0, 0);
        }

        const double gb = 1024d * 1024 * 1024;
        return ((s.TotalPhys - s.AvailPhys) / gb, s.TotalPhys / gb);
    }

    private static PerformanceCounter? TryCounter(string category, string counter, string instance)
    {
        try
        {
            return PerformanceCounterCategory.Exists(category) ? new PerformanceCounter(category, counter, instance, readOnly: true) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            Log.Info($"Счётчик {category}\\{counter} недоступен: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        foreach (var c in _gpu.Values)
        {
            c.Dispose();
        }
    }
}
