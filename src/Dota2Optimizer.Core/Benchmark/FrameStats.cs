using System.Globalization;

namespace Dota2Optimizer.Core.Benchmark;

public sealed record FrameStats(int Frames, double DurationSec, double AvgFps, double OnePercentLowFps, double PointOnePercentLowFps, int Stutters, double MedianFrameMs)
{
    /// <summary>
    /// Computes stats from frame times in milliseconds.
    /// 1% low = average FPS of the slowest 1% of frames (the definition used by most reviewers).
    /// A stutter is a frame taking more than twice the median frame time and at least 25 ms.
    /// </summary>
    public static FrameStats? FromFrameTimes(IReadOnlyList<double> frameTimesMs)
    {
        var ft = frameTimesMs.Where(t => t > 0 && t < 5000).ToList();
        if (ft.Count < 10)
        {
            return null;
        }

        var total = ft.Sum();
        var sorted = ft.OrderByDescending(t => t).ToList();
        var median = sorted[sorted.Count / 2];

        double LowFps(double fraction)
        {
            var n = Math.Max(1, (int)Math.Ceiling(sorted.Count * fraction));
            return 1000.0 / sorted.Take(n).Average();
        }

        var stutters = ft.Count(t => t > Math.Max(2 * median, 25));
        return new FrameStats(ft.Count, total / 1000.0, 1000.0 * ft.Count / total, LowFps(0.01), LowFps(0.001), stutters, median);
    }
}

/// <summary>Reads PresentMon CSV output (both 1.x and 2.x column sets).</summary>
public static class PresentMonCsv
{
    private static readonly string[] FrameTimeColumns = { "MsBetweenPresents", "FrameTime", "msBetweenPresents" };

    /// <summary>
    /// Frames with their capture time in seconds, for per-minute FPS. PresentMon 2.x writes CPUStartTime
    /// in milliseconds since capture start; 1.x writes TimeInSeconds.
    /// </summary>
    public static IReadOnlyList<(double TimeSec, double FrameMs)> ReadTimedFrames(string csv, string processName = "dota2.exe")
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return Array.Empty<(double, double)>();
        }

        var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var ft = Array.FindIndex(header, h => FrameTimeColumns.Contains(h, StringComparer.OrdinalIgnoreCase));
        var app = Array.FindIndex(header, h => h.Equals("Application", StringComparison.OrdinalIgnoreCase));
        var tMs = Array.FindIndex(header, h => h.Equals("CPUStartTime", StringComparison.OrdinalIgnoreCase));
        var tSec = Array.FindIndex(header, h => h.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));
        if (ft < 0 || (tMs < 0 && tSec < 0))
        {
            return Array.Empty<(double, double)>();
        }

        var result = new List<(double, double)>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            if (app >= 0 && app < cells.Length && !cells[app].Trim().Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ti = tMs >= 0 ? tMs : tSec;
            if (ti < cells.Length && ft < cells.Length &&
                double.TryParse(cells[ti], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) &&
                double.TryParse(cells[ft], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                result.Add((tMs >= 0 ? t / 1000.0 : t, f));
            }
        }

        return result;
    }

    public static IReadOnlyList<double> ReadFrameTimes(string csv, string processName = "dota2.exe")
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return Array.Empty<double>();
        }

        var header = lines[0].Split(',');
        var col = Array.FindIndex(header, h => FrameTimeColumns.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));
        var app = Array.FindIndex(header, h => h.Trim().Equals("Application", StringComparison.OrdinalIgnoreCase));
        if (col < 0)
        {
            return Array.Empty<double>();
        }

        var result = new List<double>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            if (cells.Length <= col)
            {
                continue;
            }

            if (app >= 0 && app < cells.Length && !cells[app].Trim().Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (double.TryParse(cells[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                result.Add(v);
            }
        }

        return result;
    }
}
