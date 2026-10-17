using System.Diagnostics;
using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Benchmark;

public sealed record BenchmarkRecord(DateTime Time, string Label, FrameStats Stats, string? Note);

public sealed record HistoryEntry(DateTime Time, string Kind, string Title, string Details);

/// <summary>
/// Frame-time capture with Intel PresentMon (MIT license, bundled by the installer as tools\PresentMon.exe).
/// PresentMon reads ETW events from the OS — it does not touch the game process, so it is VAC-safe.
/// ETW tracing requires administrator rights, so the capture runs through a UAC prompt.
/// </summary>
public static class BenchmarkService
{
    public static string? FindPresentMon()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tools");
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir, "PresentMon*.exe").OrderByDescending(f => f).FirstOrDefault();
    }

    public static string ManualInstructions =>
        "Замер вручную (если PresentMon недоступен):\n" +
        "1) Добавьте параметр запуска -console, в игре откройте консоль (~) и введите cl_showfps 1 (или включите отображение FPS в настройках).\n" +
        "2) Выберите одну и ту же сцену: демо героя или повтор одного матча на одной и той же минуте (лучше драка 5 на 5).\n" +
        "3) Наблюдайте 60 секунд, запишите средний и минимальный FPS. Повторите 2–3 раза.\n" +
        "4) Примените изменения, перезапустите игру и повторите замер в той же сцене. Сравнивайте минимальный FPS — он важнее среднего.";

    /// <summary>Runs PresentMon elevated for <paramref name="seconds"/> while Dota 2 is running and returns the parsed stats.</summary>
    public static async Task<FrameStats?> CaptureAsync(string presentMon, int seconds, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Benchmarks);
        var csv = Path.Combine(AppPaths.Benchmarks, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var args = $"--process_name dota2.exe --output_file \"{csv}\" --timed {seconds} --terminate_after_timed --stop_existing_session --no_console_stats";
        var exit = await Task.Run(() => ElevatedProcess.RunAs(presentMon, args, TimeSpan.FromSeconds(seconds + 30)), ct).ConfigureAwait(false);
        if (exit == -1 || !File.Exists(csv))
        {
            Log.Warn($"PresentMon не создал файл (код {exit})");
            return null;
        }

        return FrameStats.FromFrameTimes(PresentMonCsv.ReadFrameTimes(await File.ReadAllTextAsync(csv, ct).ConfigureAwait(false)));
    }

    public static bool DotaRunning()
    {
        var p = Process.GetProcessesByName("dota2");
        foreach (var x in p)
        {
            x.Dispose();
        }

        return p.Length > 0;
    }
}

/// <summary>History of applied changes and benchmark results, stored locally as JSON.</summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly IFileSystem _fs;
    private readonly string _path;

    public HistoryStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    private sealed class Data
    {
        public List<HistoryEntry> Entries { get; set; } = new();
        public List<BenchmarkRecord> Benchmarks { get; set; } = new();
    }

    private Data Load() =>
        _fs.FileExists(_path) ? JsonSerializer.Deserialize<Data>(_fs.ReadAllText(_path), Options) ?? new Data() : new Data();

    private void Save(Data d) => _fs.WriteAllText(_path, JsonSerializer.Serialize(d, Options));

    public IReadOnlyList<HistoryEntry> Entries => Load().Entries;
    public IReadOnlyList<BenchmarkRecord> Benchmarks => Load().Benchmarks;

    public void Add(HistoryEntry e)
    {
        var d = Load();
        d.Entries.Add(e);
        Save(d);
    }

    public void Add(BenchmarkRecord r)
    {
        var d = Load();
        d.Benchmarks.Add(r);
        Save(d);
    }

    /// <summary>Human-readable before/after comparison of the last "до" and "после" runs.</summary>
    public static string Compare(BenchmarkRecord before, BenchmarkRecord after)
    {
        static string Pct(double a, double b) => a <= 0 ? "—" : $"{(b - a) / a * 100:+0.0;-0.0;0}%";
        return $"Средний FPS: {before.Stats.AvgFps:0} → {after.Stats.AvgFps:0} ({Pct(before.Stats.AvgFps, after.Stats.AvgFps)})\n" +
               $"1% low: {before.Stats.OnePercentLowFps:0} → {after.Stats.OnePercentLowFps:0} ({Pct(before.Stats.OnePercentLowFps, after.Stats.OnePercentLowFps)})\n" +
               $"Просадки (кадры > 2× медианы): {before.Stats.Stutters} → {after.Stats.Stutters}\n" +
               "Разница меньше 3–5% обычно в пределах погрешности замера.";
    }
}
