using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Dota2Optimizer.Core.Network;

public sealed record DnsResult(string Name, string Server, double? MedianMs, int Failed, int Total)
{
    public bool Works => MedianMs is not null;
}

/// <summary>
/// Measures how fast a DNS server answers: plain UDP queries (type A) for a few popular names, median of the replies.
/// Only runs on the user's click; sends nothing but the DNS questions themselves.
/// </summary>
public static class DnsBenchmark
{
    public static readonly (string Name, string Server)[] Presets =
    {
        ("Cloudflare", "1.1.1.1"),
        ("Google", "8.8.8.8"),
        ("Quad9", "9.9.9.9"),
    };

    private static readonly string[] Names = { "steampowered.com", "google.com", "discord.com", "youtube.com", "microsoft.com", "riotgames.com" };

    public static async Task<DnsResult> MeasureAsync(string name, string server, CancellationToken ct, int rounds = 2, int timeoutMs = 1500)
    {
        var times = new List<double>();
        var failed = 0;
        for (var r = 0; r < rounds; r++)
        {
            foreach (var host in Names)
            {
                ct.ThrowIfCancellationRequested();
                var t = await QueryAsync(server, host, timeoutMs, ct);
                if (t is { } ms)
                {
                    times.Add(ms);
                }
                else
                {
                    failed++;
                }
            }
        }

        return new DnsResult(name, server, Median(times), failed, rounds * Names.Length);
    }

    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var s = values.OrderBy(v => v).ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }

    /// <summary>Builds a standard DNS query: header with recursion desired, one question, type A, class IN.</summary>
    public static byte[] BuildQuery(ushort id, string host)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }

        bytes.AddRange(new byte[] { 0, 0, 1, 0, 1 });
        return bytes.ToArray();
    }

    /// <summary>True when the reply matches the query id and is a response without a server failure.</summary>
    public static bool IsAnswer(byte[] reply, ushort id) =>
        reply.Length >= 12 && reply[0] == (byte)(id >> 8) && reply[1] == (byte)id && (reply[2] & 0x80) != 0 && (reply[3] & 0x0F) != 2;

    private static async Task<double?> QueryAsync(string server, string host, int timeoutMs, CancellationToken ct)
    {
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            var query = BuildQuery(id, host);
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse(server), 53), cts.Token);
            while (true)
            {
                var reply = await udp.ReceiveAsync(cts.Token);
                if (IsAnswer(reply.Buffer, id))
                {
                    return sw.Elapsed.TotalMilliseconds;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
