using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Benchmark;

/// <summary>One benchmark run: frame statistics from PresentMon and ping measured at the same time.</summary>
public sealed record BenchResult
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; init; } = DateTime.Now;
    public string Game { get; init; } = string.Empty;

    /// <summary>"before" or "after".</summary>
    public string Label { get; init; } = "before";

    public double AvgFps { get; init; }
    public double Low1Fps { get; init; }
    public double Low01Fps { get; init; }
    public double MedianFrameMs { get; init; }
    public int Stutters { get; init; }
    public double DurationSec { get; init; }
    public double? PingMs { get; init; }
    public double? JitterMs { get; init; }

    /// <summary>Frame times downsampled to a few hundred points for the chart.</summary>
    public IReadOnlyList<double> FrameTimes { get; init; } = Array.Empty<double>();

    public static BenchResult From(string game, string label, FrameStats stats, IReadOnlyList<double> frameTimes, double? ping, double? jitter) => new()
    {
        Game = game,
        Label = label,
        AvgFps = stats.AvgFps,
        Low1Fps = stats.OnePercentLowFps,
        Low01Fps = stats.PointOnePercentLowFps,
        MedianFrameMs = stats.MedianFrameMs,
        Stutters = stats.Stutters,
        DurationSec = stats.DurationSec,
        PingMs = ping,
        JitterMs = jitter,
        FrameTimes = Downsample(frameTimes, 300),
    };

    /// <summary>Keeps the worst frame of each bucket, so spikes stay visible on the chart.</summary>
    public static IReadOnlyList<double> Downsample(IReadOnlyList<double> values, int points)
    {
        if (values.Count <= points)
        {
            return values.ToList();
        }

        var result = new List<double>(points);
        var step = values.Count / (double)points;
        for (var i = 0; i < points; i++)
        {
            var from = (int)(i * step);
            var to = Math.Min(values.Count, (int)((i + 1) * step));
            result.Add(values.Skip(from).Take(Math.Max(1, to - from)).Max());
        }

        return result;
    }
}

public sealed record BenchDelta(string Metric, double Before, double After, double Percent, bool HigherIsBetter)
{
    /// <summary>Changes under 3% are within run-to-run noise of a 60-second capture.</summary>
    public bool Noise => Math.Abs(Percent) < 3;

    public bool Better => !Noise && (HigherIsBetter ? Percent > 0 : Percent < 0);
}

public static class BenchCompare
{
    public static IReadOnlyList<BenchDelta> Deltas(BenchResult before, BenchResult after)
    {
        var list = new List<BenchDelta>
        {
            Delta("Средний FPS", before.AvgFps, after.AvgFps, true),
            Delta("1% low", before.Low1Fps, after.Low1Fps, true),
            Delta("0,1% low", before.Low01Fps, after.Low01Fps, true),
            Delta("Время кадра (медиана), мс", before.MedianFrameMs, after.MedianFrameMs, false),
        };
        if (before.PingMs is { } p1 && after.PingMs is { } p2)
        {
            list.Add(Delta("Пинг, мс", p1, p2, false));
        }

        return list;
    }

    /// <summary>One honest sentence about the result.</summary>
    public static string Verdict(IReadOnlyList<BenchDelta> deltas)
    {
        var fps = deltas[0];
        var low = deltas[1];
        if (fps.Noise && low.Noise)
        {
            return "Разница в пределах погрешности замера (меньше 3%): изменения на FPS почти не повлияли. Повторите замер в той же сцене, чтобы убедиться.";
        }

        var parts = new List<string>();
        if (!fps.Noise)
        {
            parts.Add($"средний FPS {(fps.Percent > 0 ? "вырос" : "упал")} на {Math.Abs(fps.Percent):0}%");
        }

        if (!low.Noise)
        {
            parts.Add($"1% low {(low.Percent > 0 ? "вырос" : "упал")} на {Math.Abs(low.Percent):0}%");
        }

        return char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts)[1..] + ". Сравнивайте замеры одной и той же сцены одной длины — иначе цифры не сопоставимы.";
    }

    private static BenchDelta Delta(string name, double a, double b, bool higherBetter) =>
        new(name, a, b, a <= 0 ? 0 : (b - a) / a * 100, higherBetter);
}

/// <summary>Benchmark history in a JSON file (last 50 runs).</summary>
public sealed class BenchmarkStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IFileSystem _fs;
    private readonly string _path;

    public BenchmarkStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    public IReadOnlyList<BenchResult> Load()
    {
        try
        {
            return _fs.FileExists(_path) ? JsonSerializer.Deserialize<List<BenchResult>>(_fs.ReadAllText(_path), Json) ?? new List<BenchResult>() : new List<BenchResult>();
        }
        catch (JsonException ex)
        {
            Log.Error("Повреждена история бенчмарков", ex);
            return new List<BenchResult>();
        }
    }

    public void Add(BenchResult r)
    {
        var all = Load().Append(r).TakeLast(50).ToList();
        _fs.WriteAllText(_path, JsonSerializer.Serialize(all, Json));
    }

    /// <summary>Latest "before" and the latest "after" taken after it, for one game.</summary>
    public (BenchResult? Before, BenchResult? After) Pair(string game)
    {
        var runs = Load().Where(r => r.Game.Equals(game, StringComparison.OrdinalIgnoreCase)).ToList();
        var before = runs.LastOrDefault(r => r.Label == "before");
        var after = before is null ? null : runs.LastOrDefault(r => r.Label == "after" && r.Time >= before.Time);
        return (before, after);
    }
}
