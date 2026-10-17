using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Crash;
using Dota2Optimizer.Core.Network;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Tests;

public class AnalysisTests
{
    [Fact]
    public void Frame_stats_compute_average_and_lows()
    {
        // 990 frames at 10 ms (100 fps) and 10 frames at 50 ms (20 fps).
        var ft = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(50.0, 10)).ToList();
        var s = FrameStats.FromFrameTimes(ft)!;

        Assert.Equal(1000, s.Frames);
        Assert.Equal(1000.0 * 1000 / (990 * 10 + 10 * 50), s.AvgFps, 3);
        Assert.Equal(20, s.OnePercentLowFps, 3);
        Assert.Equal(10, s.Stutters);
        Assert.Equal(10, s.MedianFrameMs);
    }

    [Fact]
    public void Frame_stats_need_enough_frames()
    {
        Assert.Null(FrameStats.FromFrameTimes(new[] { 10.0, 10.0 }));
    }

    [Fact]
    public void PresentMon_csv_filters_process_and_reads_column()
    {
        var csv = "Application,ProcessID,MsBetweenPresents,MsGPUTime\n" +
                  "dota2.exe,100,6.94,4.0\n" +
                  "dwm.exe,50,16.6,1.0\n" +
                  "dota2.exe,100,7.10,4.1\n";
        Assert.Equal(new[] { 6.94, 7.10 }, PresentMonCsv.ReadFrameTimes(csv));
    }

    [Fact]
    public void Ping_stats_loss_and_jitter()
    {
        var s = PingStats.From(new long?[] { 10, 20, null, 10 });
        Assert.Equal(25, s.LossPercent);
        Assert.Equal(40.0 / 3, s.AvgMs!.Value, 3);
        Assert.Equal(10, s.JitterMs!.Value, 3); // |20-10|, |10-20|
    }

    private static TargetResult T(TargetKind k, double loss, double? avg, double jitter = 1) =>
        new(k.ToString(), "h", k, new PingStats(100, (int)(100 - loss), loss, avg, avg, avg, avg is null ? null : jitter));

    [Fact]
    public void Verdict_local_when_gateway_is_bad()
    {
        var v = NetworkAnalysis.Evaluate(new[] { T(TargetKind.Gateway, 8, 3), T(TargetKind.Internet, 8, 30) }, isWifi: true);
        Assert.Equal(VerdictKind.Local, v.Kind);
    }

    [Fact]
    public void Verdict_provider_when_only_internet_is_bad()
    {
        var v = NetworkAnalysis.Evaluate(new[] { T(TargetKind.Gateway, 0, 1), T(TargetKind.Internet, 6, 40) }, isWifi: false);
        Assert.Equal(VerdictKind.Provider, v.Kind);
    }

    [Fact]
    public void Verdict_server_when_only_valve_is_bad()
    {
        var v = NetworkAnalysis.Evaluate(new[] { T(TargetKind.Gateway, 0, 1), T(TargetKind.Internet, 0, 20), T(TargetKind.Valve, 5, 60) }, isWifi: false);
        Assert.Equal(VerdictKind.ServerOrRoute, v.Kind);
    }

    [Fact]
    public void Verdict_inconclusive_when_valve_ignores_icmp()
    {
        var v = NetworkAnalysis.Evaluate(new[] { T(TargetKind.Gateway, 0, 1), T(TargetKind.Internet, 0, 20), T(TargetKind.Valve, 100, null) }, isWifi: false);
        Assert.Equal(VerdictKind.Inconclusive, v.Kind);
    }

    [Fact]
    public void Verdict_ok()
    {
        var v = NetworkAnalysis.Evaluate(new[] { T(TargetKind.Gateway, 0, 1), T(TargetKind.Internet, 0, 20), T(TargetKind.Valve, 0, 45) }, isWifi: false);
        Assert.Equal(VerdictKind.Ok, v.Kind);
    }

    [Fact]
    public void Parses_wlan_output_in_russian_and_english()
    {
        var ru = "    Имя                    : Беспроводная сеть\n    SSID                   : Home\n    Тип радио              : 802.11ax\n    Сигнал                 : 76%\n";
        var en = "    Name                   : Wi-Fi\n    SSID                   : Office\n    Radio type             : 802.11ac\n    Signal                 : 54%\n";
        Assert.Equal((76, "Home", "802.11ax"), NetworkAnalysis.ParseWlan(ru));
        Assert.Equal((54, "Office", "802.11ac"), NetworkAnalysis.ParseWlan(en));
    }

    [Fact]
    public void Parses_sdr_config()
    {
        var json = "{\"revision\":1,\"pops\":{\"sto\":{\"desc\":\"Stockholm\",\"relays\":[{\"ipv4\":\"1.2.3.4\",\"port_range\":[27015,27060]}]},\"xyz\":{\"desc\":\"No relays\"}}}";
        var pops = NetworkAnalysis.ParseSdrConfig(json);
        var pop = Assert.Single(pops);
        Assert.Equal("sto", pop.Code);
        Assert.Equal("Stockholm", pop.Description);
        Assert.Equal("1.2.3.4", pop.Ipv4);
    }

    [Fact]
    public void Crash_classifier_maps_modules_and_tdr()
    {
        var events = new[]
        {
            new RawEvent(new DateTime(2026, 10, 1), "Application", "Application Error", 1000, "dota2.exe crashed",
                new[] { "dota2.exe", "1.0", "0", "nvwgf2umx.dll", "32.0", "0", "c0000005" }),
            new RawEvent(new DateTime(2026, 10, 2), "Application", "Application Error", 1000, "dota2.exe crashed",
                new[] { "dota2.exe", "1.0", "0", "nvwgf2umx.dll", "32.0", "0", "c0000005" }),
            new RawEvent(new DateTime(2026, 10, 3), "System", "Display", 4101, "Display driver nvlddmkm stopped responding", Array.Empty<string>()),
            new RawEvent(new DateTime(2026, 10, 3), "Application", "Application Error", 1000, "chrome.exe crashed",
                new[] { "chrome.exe", "1.0", "0", "chrome.dll", "1", "0", "c0000005" }),
            new RawEvent(new DateTime(2026, 10, 4), "System", "Microsoft-Windows-Kernel-Power", 41, "rebooted", Array.Empty<string>()),
            new RawEvent(new DateTime(2026, 10, 4), "Application", "Application Error", 1000, "dota2.exe crashed",
                new[] { "dota2.exe", "1.0", "0", "steamclient64.dll", "1", "0", "c0000005" }),
        };

        var findings = CrashClassifier.Classify(events);

        var nv = Assert.Single(findings, f => f.Category == "Драйвер NVIDIA");
        Assert.Equal(2, nv.Count);
        Assert.Contains(findings, f => f.Category == "Видеокарта (TDR)");
        Assert.Contains(findings, f => f.Category == "Питание");
        Assert.DoesNotContain(findings, f => f.Title.Contains("chrome", StringComparison.OrdinalIgnoreCase));
        // steamclient64.dll must not be mistaken for the game's client.dll
        Assert.DoesNotContain(findings, f => f.Category == "Файлы игры");
        Assert.Contains(findings, f => f.Category == "Dota 2" && f.Title.Contains("steamclient64.dll"));
    }

    [Fact]
    public void Log_analyzer_finds_typical_errors()
    {
        var log = "Loading map\nDXGI_ERROR_DEVICE_REMOVED in present\nUnknown command \"cl_interp_ratio\"\nfailed to allocate 512 MB\n";
        var findings = LogAnalyzer.Analyze(log);
        Assert.Equal(Severity.Critical, findings[0].Severity);
        Assert.Contains(findings, f => f.Title.Contains("DEVICE_REMOVED"));
        Assert.Contains(findings, f => f.Title.Contains("памяти"));
        Assert.Contains(findings, f => f.Severity == Severity.Info);
    }
}
