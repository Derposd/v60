using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.Network;

public enum TargetKind
{
    Gateway,
    Internet,
    Valve,
}

public sealed record PingStats(int Sent, int Received, double LossPercent, double? AvgMs, double? MinMs, double? MaxMs, double? JitterMs)
{
    /// <summary>Jitter is the mean absolute difference between consecutive replies (RFC 3550 style, unsmoothed).</summary>
    public static PingStats From(IReadOnlyList<long?> rtts)
    {
        var ok = rtts.Where(r => r.HasValue).Select(r => (double)r!.Value).ToList();
        var sent = rtts.Count;
        var loss = sent == 0 ? 0 : 100.0 * (sent - ok.Count) / sent;
        if (ok.Count == 0)
        {
            return new PingStats(sent, 0, loss, null, null, null, null);
        }

        double? jitter = ok.Count < 2 ? 0 : ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new PingStats(sent, ok.Count, loss, ok.Average(), ok.Min(), ok.Max(), jitter);
    }

    public override string ToString() =>
        AvgMs is null ? $"нет ответа ({LossPercent:0}% потерь)" : $"{AvgMs:0} мс, потери {LossPercent:0.#}%, jitter {JitterMs:0.#} мс";
}

public sealed record TargetResult(string Name, string Host, TargetKind Kind, PingStats Stats);

public enum VerdictKind
{
    Ok,
    Local,
    Provider,
    ServerOrRoute,
    Inconclusive,
}

public sealed record NetworkVerdict(VerdictKind Kind, string Title, string Details);

public static partial class NetworkAnalysis
{
    public const double LossThreshold = 2.0;

    public static NetworkVerdict Evaluate(IReadOnlyList<TargetResult> results, bool isWifi)
    {
        var gateway = results.Where(r => r.Kind == TargetKind.Gateway).Select(r => r.Stats).ToList();
        var internet = results.Where(r => r.Kind == TargetKind.Internet).Select(r => r.Stats).ToList();
        var valve = results.Where(r => r.Kind == TargetKind.Valve).Select(r => r.Stats).ToList();

        static bool Bad(PingStats s, double maxAvg, double maxJitter) =>
            s.LossPercent > LossThreshold || s.AvgMs is null || s.AvgMs > maxAvg || (s.JitterMs ?? 0) > maxJitter;

        if (gateway.Count > 0 && gateway.All(g => Bad(g, isWifi ? 15 : 5, isWifi ? 10 : 5)))
        {
            return new NetworkVerdict(VerdictKind.Local, "Проблема у вас: домашняя сеть",
                isWifi
                    ? "Потери или скачки пинга уже до роутера. Это Wi-Fi: подключите ПК кабелем, перенесите роутер ближе, смените канал/диапазон 5 ГГц, проверьте, не качает ли кто-то в сети."
                    : "Потери уже до роутера по кабелю: проверьте кабель и порт, перезагрузите роутер, проверьте фоновые загрузки на других устройствах.");
        }

        if (internet.Count > 0 && internet.All(i => Bad(i, 80, 20)))
        {
            return new NetworkVerdict(VerdictKind.Provider, "Проблема у провайдера (или на его маршруте)",
                "До роутера всё стабильно, но потери или высокий пинг дальше. Приложение это исправить не может: позвоните провайдеру и сообщите время и процент потерь (результаты можно скопировать).");
        }

        var valveResponded = valve.Where(v => v.Received > 0).ToList();
        if (valve.Count > 0 && valveResponded.Count == 0)
        {
            return new NetworkVerdict(VerdictKind.Inconclusive, "Сеть до интернета в порядке, серверы Valve не отвечают на ping",
                "Релеи Valve могут не отвечать на ICMP — это не значит, что они недоступны. Проверьте пинг до регионов в самой игре: Поиск игры → Регион (пинг показан рядом с каждым регионом).");
        }

        if (valveResponded.Count > 0 && valveResponded.All(v => Bad(v, 150, 25)))
        {
            return new NetworkVerdict(VerdictKind.ServerOrRoute, "Проблема на маршруте до серверов Valve или на стороне сервера",
                "До провайдера всё хорошо, но до серверов Valve пинг высокий или есть потери. Выберите ближайший регион в игре; если проблема у всех — это сервер, приложение это не исправит.");
        }

        return new NetworkVerdict(VerdictKind.Ok, "Сеть в порядке",
            isWifi ? "Потерь нет. Для минимальной задержки всё равно лучше кабель вместо Wi-Fi." : "Потерь нет, пинг стабильный. Если в игре лагает — проверьте регион поиска и фоновые загрузки.");
    }

    [GeneratedRegex(@"^\s*(Signal|Сигнал|Signalqualität|Signal quality)\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex WifiSignal();

    [GeneratedRegex(@"^\s*(SSID)\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex WifiSsid();

    [GeneratedRegex(@"^\s*(Radio type|Тип радио(модуля)?)\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex WifiRadio();

    /// <summary>Parses "netsh wlan show interfaces" (English or Russian output).</summary>
    public static (int? SignalPercent, string? Ssid, string? Radio) ParseWlan(string output)
    {
        var s = WifiSignal().Match(output);
        var ssid = WifiSsid().Match(output);
        var radio = WifiRadio().Match(output);
        return (s.Success ? int.Parse(s.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
            ssid.Success ? ssid.Groups[2].Value.Trim() : null,
            radio.Success ? radio.Groups[3].Value.Trim() : null);
    }

    public sealed record SdrPop(string Code, string Description, string? Ipv4);

    /// <summary>Parses Valve's public SDR config (ISteamApps/GetSDRConfig): "pops" → {code: {desc, relays:[{ipv4}]}}.</summary>
    public static IReadOnlyList<SdrPop> ParseSdrConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("pops", out var pops) || pops.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<SdrPop>();
        }

        var result = new List<SdrPop>();
        foreach (var pop in pops.EnumerateObject())
        {
            var desc = pop.Value.TryGetProperty("desc", out var d) ? d.GetString() ?? pop.Name : pop.Name;
            string? ip = null;
            if (pop.Value.TryGetProperty("relays", out var relays) && relays.ValueKind == JsonValueKind.Array)
            {
                ip = relays.EnumerateArray()
                    .Select(r => r.TryGetProperty("ipv4", out var v) ? v.GetString() : null)
                    .FirstOrDefault(x => !string.IsNullOrEmpty(x));
            }

            if (ip is not null)
            {
                result.Add(new SdrPop(pop.Name, desc, ip));
            }
        }

        return result;
    }
}
