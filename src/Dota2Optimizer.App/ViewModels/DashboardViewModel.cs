using System.Windows.Threading;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>
/// Live CPU / GPU / RAM / temperature rings on the home page. Samples every 2 s only while the home page is open and the window
/// is not minimised, so the app costs nothing in the background.
/// </summary>
public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private const int HistoryLength = 60;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Queue<double> _cpuHistory = new();
    private LiveMetrics? _metrics;
    private LiveSample? _last;
    private Task<LiveSample>? _pending;
    private (double Used, double Total) _ram;

    public DashboardViewModel()
    {
        _timer.Tick += async (_, _) => await SampleAsync();
    }

    public double Cpu => _last?.CpuPercent ?? double.NaN;
    public double Gpu => _last?.GpuPercent ?? double.NaN;
    public double Ram => _ram.Total > 0 ? _ram.Used / _ram.Total * 100 : double.NaN;
    public double Temp => _last?.TempC ?? double.NaN;
    public bool HasTemp => _last?.TempC is not null;
    public string RamText => _ram.Total > 0 ? $"{_ram.Used:0.0} из {_ram.Total:0} ГБ" : "—";
    public string GpuText => _last?.GpuPercent is null ? "счётчик недоступен" : "все видеокарты, 3D";
    public string TempText => HasTemp ? "датчик ACPI" : "датчик не найден";
    public IReadOnlyList<double> CpuHistory { get; private set; } = Array.Empty<double>();

    public bool Active
    {
        set
        {
            if (value)
            {
                _metrics ??= new LiveMetrics();
                if (!_timer.IsEnabled)
                {
                    _timer.Start();
                    _ = SampleAsync();
                }
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    /// <summary>
    /// Memory is read every tick right here. Counters (CPU, GPU, temperature) run in the background; a slow or stuck sensor only
    /// delays its own ring, the next tick doesn't queue another query while one is pending.
    /// </summary>
    private async Task SampleAsync()
    {
        if (_metrics is null)
        {
            return;
        }

        _ram = LiveMetrics.Memory();
        OnPropertyChanged(nameof(Ram));
        OnPropertyChanged(nameof(RamText));
        if (_pending is { IsCompleted: false })
        {
            return;
        }

        var m = _metrics;
        _pending = Task.Run(m.Sample);
        _last = await _pending;
        if (_last.CpuPercent is { } c)
        {
            _cpuHistory.Enqueue(c);
            while (_cpuHistory.Count > HistoryLength)
            {
                _cpuHistory.Dequeue();
            }

            CpuHistory = _cpuHistory.ToArray();
        }

        foreach (var p in new[] { nameof(Cpu), nameof(Gpu), nameof(Temp), nameof(HasTemp), nameof(GpuText), nameof(TempText), nameof(CpuHistory) })
        {
            OnPropertyChanged(p);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _metrics?.Dispose();
    }
}
