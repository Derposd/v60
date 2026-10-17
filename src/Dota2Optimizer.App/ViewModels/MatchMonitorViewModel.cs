using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.App.ViewModels;

public sealed record FpsBar(string Label, double Fps, double Height, string Tooltip);

/// <summary>Records a whole match and explains late-game FPS drops.</summary>
public sealed class MatchMonitorViewModel : ObservableObject
{
    private const double ChartHeight = 140;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private MatchMonitor? _monitor;
    private string? _csv;
    private bool _running;
    private string _status = "Запустите Dota 2, затем нажмите «Начать запись» и сыграйте матч целиком. В конце нажмите «Стоп и анализ» или просто закройте игру.";
    private string _elapsed = "—";
    private string _cpu = "—";
    private string _gpu = "—";
    private string _ram = "—";
    private string _vram = "—";
    private string _free = "—";
    private string? _summary;

    public MatchMonitorViewModel(AppServices services)
    {
        _s = services;
        StartCommand = new AsyncCommand(StartAsync, () => !_running);
        StopCommand = new AsyncCommand(StopAsync, () => _running);
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ObservableCollection<Advice> Findings { get; } = new();
    public ObservableCollection<FpsBar> Bars { get; } = new();

    public bool HasPresentMon => BenchmarkService.FindPresentMon() is not null;

    public bool Running
    {
        get => _running;
        private set => Set(ref _running, value);
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Elapsed { get => _elapsed; private set => Set(ref _elapsed, value); }
    public string Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public string Gpu { get => _gpu; private set => Set(ref _gpu, value); }
    public string Ram { get => _ram; private set => Set(ref _ram, value); }
    public string Vram { get => _vram; private set => Set(ref _vram, value); }
    public string FreeRam { get => _free; private set => Set(ref _free, value); }
    public string? Summary { get => _summary; private set => Set(ref _summary, value); }

    public string Explanation =>
        "Что записывается: частота процессора относительно базовой (падает при перегреве), загрузка CPU и видеокарты, свободная ОЗУ, память и видеопамять игры — " +
        "из системных счётчиков Windows, к процессу игры программа не обращается. FPS по минутам пишет PresentMon (нужны права администратора). " +
        "Данные остаются на компьютере.";

    private async Task StartAsync()
    {
        if (!SystemScanner.IsProcessRunning("dota2"))
        {
            Ui.Info("Сначала запустите Dota 2 (можно в меню), затем начните запись.");
            return;
        }

        Findings.Clear();
        Bars.Clear();
        Summary = null;
        _monitor?.Dispose();
        _monitor = await Task.Run(() => new MatchMonitor());
        Running = true;
        _csv = null;

        var pm = BenchmarkService.FindPresentMon();
        if (pm is not null)
        {
            Directory.CreateDirectory(AppPaths.Benchmarks);
            _csv = Path.Combine(AppPaths.Benchmarks, $"match-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _ = MatchMonitor.StartFpsCaptureAsync(pm, _csv);
            Status = "Идёт запись. Если Windows спросит права администратора — разрешите, это нужно для записи FPS. Можно свернуть программу.";
        }
        else
        {
            Status = "Идёт запись (без FPS: PresentMon не найден — выводы будут по нагреву, памяти и загрузке).";
        }

        await TickAsync();
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (_monitor is null)
        {
            return;
        }

        var sample = await Task.Run(_monitor.Sample);
        if (sample is null)
        {
            if (_monitor.Samples.Count > 0)
            {
                Status = "Dota 2 закрыта — запись остановлена.";
                await StopAsync();
            }

            return;
        }

        var t = TimeSpan.FromSeconds(sample.ElapsedSec);
        Elapsed = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        Cpu = $"{sample.CpuPerformancePct:0}% частоты · {sample.CpuUtilityPct:0}% загрузка";
        Gpu = $"{sample.GpuUtilPct:0}%";
        Ram = $"{sample.DotaPrivateMb:0} МБ";
        Vram = $"{sample.DotaVramMb:0} МБ";
        FreeRam = $"{sample.AvailableRamMb:0} МБ";
    }

    private async Task StopAsync()
    {
        _timer.Stop();
        Running = false;
        var monitor = _monitor;
        if (monitor is null)
        {
            return;
        }

        var minutes = new List<MinuteFps>();
        if (_csv is not null && File.Exists(_csv))
        {
            try
            {
                // PresentMon may still be writing (it stops by itself when Dota closes), so open shared.
                await using var fs = new FileStream(_csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                var text = await reader.ReadToEndAsync();
                minutes = MatchAnalyzer.FpsPerMinute(PresentMonCsv.ReadTimedFrames(text)).ToList();
            }
            catch (IOException ex)
            {
                Log.Warn($"Не удалось прочитать запись FPS: {ex.Message}");
            }
        }

        var vram = (_s.Snapshot?.PrimaryGpu?.VramGb ?? 0) * 1024;
        var ram = (_s.Snapshot?.Memory?.TotalGb ?? 0) * 1024;
        var report = MatchAnalyzer.Analyze(monitor.Samples, minutes, vram, ram);
        Summary = report.Summary;
        foreach (var f in report.Findings)
        {
            Findings.Add(f);
        }

        var max = Math.Max(1, minutes.Count == 0 ? 1 : minutes.Max(m => m.AvgFps));
        foreach (var m in minutes)
        {
            Bars.Add(new FpsBar($"{m.Minute}", m.AvgFps, Math.Max(2, m.AvgFps / max * ChartHeight),
                $"{m.Minute}-я минута: средний {m.AvgFps:0} FPS, 1% low {m.OnePercentLowFps:0}"));
        }

        _s.History.Add(new HistoryEntry(DateTime.Now, "Монитор матча", report.Summary,
            string.Join("; ", report.Findings.Select(f => f.Title)) + (minutes.Count > 0 ? " | " + MatchAnalyzer.FormatMinutes(minutes) : string.Empty)));
        if (minutes.Count > 0 && SystemScanner.IsProcessRunning("dota2"))
        {
            Status = "Анализ готов. Запись FPS PresentMon завершится сама, когда вы закроете Dota 2.";
        }
        else if (Status.StartsWith("Идёт", StringComparison.Ordinal))
        {
            Status = "Анализ готов.";
        }

        monitor.Dispose();
        _monitor = null;
    }
}
