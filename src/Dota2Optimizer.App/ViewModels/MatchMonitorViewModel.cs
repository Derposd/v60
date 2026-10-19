using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Game;
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
    private readonly GameDefinition _game;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private MatchMonitor? _monitor;
    private FpsCapture? _capture;
    private string? _noPresentMon;
    private bool _running;
    private string _status = "Запустите игру, затем нажмите «Начать запись» и сыграйте матч целиком. В конце нажмите «Стоп и анализ» или просто закройте игру.";
    private string _elapsed = "—";
    private string _cpu = "—";
    private string _gpu = "—";
    private string _ram = "—";
    private string _vram = "—";
    private string _free = "—";
    private string? _summary;

    public MatchMonitorViewModel(AppServices services, GameDefinition game)
    {
        _s = services;
        _game = game;
        StartCommand = new AsyncCommand(StartAsync, () => !_running);
        StopCommand = new AsyncCommand(StopAsync, () => _running);
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public string GameName => _game.Name;

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ObservableCollection<Advice> Findings { get; } = new();
    public ObservableCollection<FpsBar> Bars { get; } = new();

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
        "из системных счётчиков Windows, к процессу игры программа не обращается. FPS по минутам пишет Intel PresentMon (встроен в программу, нужны права администратора — Windows спросит один раз). " +
        "Данные остаются на компьютере.";

    private async Task StartAsync()
    {
        if (!SystemScanner.IsProcessRunning(_game.ProcessName))
        {
            Ui.Info($"Сначала запустите {_game.Name} (можно в меню), затем начните запись.");
            return;
        }

        Findings.Clear();
        Bars.Clear();
        Summary = null;
        _monitor?.Dispose();
        _monitor = await Task.Run(() => new MatchMonitor(_game.ProcessName));
        Running = true;
        _capture = null;
        _noPresentMon = null;

        var pm = await Task.Run(PresentMonTool.Find);
        if (pm is not null)
        {
            _capture = await Task.Run(() => FpsCapture.Start(pm, _game.ExeFileName));
            Status = _capture.Problem(0) is { } early && (_capture.Declined || _capture.StartError is not null)
                ? $"Идёт запись без FPS: {early}. Выводы будут по нагреву, памяти и загрузке."
                : "Идёт запись. Можно свернуть программу и играть — в конце нажмите «Стоп и анализ» или просто закройте игру.";
        }
        else
        {
            _noPresentMon = "PresentMon не найден (сборка без него) — выводы будут по нагреву, памяти и загрузке";
            Status = $"Идёт запись без FPS: {_noPresentMon}.";
        }

        await TickAsync();
        _timer.Start();
        _ = WatchCaptureStartAsync(_capture);
    }

    /// <summary>PresentMon fails fast when it can't start tracing; tell the user right away instead of at the end of the match.</summary>
    private async Task WatchCaptureStartAsync(FpsCapture? capture)
    {
        if (capture is null || capture.Declined || capture.StartError is not null)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(6));
        if (capture == _capture && Running && capture.HasExited && SystemScanner.IsProcessRunning(_game.ProcessName))
        {
            Status = $"Идёт запись без FPS: {capture.Problem(0)}. Выводы будут по нагреву, памяти и загрузке.";
        }
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
                Status = $"{_game.Name} закрыта — запись остановлена.";
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
        var frames = 0;
        // PresentMon may still be writing (it stops by itself when the game closes); the capture reads the file shared.
        if (_capture is not null && await Task.Run(_capture.ReadCsv) is { } text)
        {
            var timed = PresentMonCsv.ReadTimedFrames(text, _game.ExeFileName);
            frames = timed.Count;
            minutes = MatchAnalyzer.FpsPerMinute(timed).ToList();
        }

        var problem = _noPresentMon ?? _capture?.Problem(frames);
        if (problem is not null)
        {
            Log.Warn($"FPS не записан: {problem}");
        }

        var vram = (_s.Snapshot?.PrimaryGpu?.VramGb ?? 0) * 1024;
        var ram = (_s.Snapshot?.Memory?.TotalGb ?? 0) * 1024;
        var report = MatchAnalyzer.Analyze(monitor.Samples, minutes, vram, ram, problem, _game);
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
        if (minutes.Count > 0 && SystemScanner.IsProcessRunning(_game.ProcessName))
        {
            Status = $"Анализ готов. Запись FPS PresentMon завершится сама, когда вы закроете {_game.Name}.";
        }
        else if (Status.StartsWith("Идёт", StringComparison.Ordinal))
        {
            Status = "Анализ готов.";
        }

        monitor.Dispose();
        _monitor = null;
    }
}
