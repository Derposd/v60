using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Network;

public sealed record AdapterInfo(string Name, string Description, NetworkInterfaceType Type, IPAddress? Gateway, IReadOnlyList<IPAddress> Dns, bool DnsIsStatic, long SpeedMbps)
{
    public bool IsWifi => Type == NetworkInterfaceType.Wireless80211;
}

public sealed record TrafficSample(double DownMbps, double UpMbps);

/// <summary>
/// The only part of the app that uses the network, and only when the user presses "Test".
/// ICMP ping to the router, public resolvers and (optionally) Valve relays.
/// </summary>
public sealed class NetworkDiagnostics
{
    public const string SdrConfigUrl = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=570";

    private readonly ICommandRunner _cmd;

    public NetworkDiagnostics(ICommandRunner cmd)
    {
        _cmd = cmd;
    }

    public static AdapterInfo? ActiveAdapter()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (Nic: n, Props: n.GetIPProperties()))
            .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ToList();
        var best = candidates.OrderBy(x => x.Props.GetIPv4Properties()?.Index ?? int.MaxValue).FirstOrDefault();
        if (best.Nic is null)
        {
            return null;
        }

        var gw = best.Props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        var dns = best.Props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
        return new AdapterInfo(best.Nic.Name, best.Nic.Description, best.Nic.NetworkInterfaceType, gw, dns, DnsIsStatic(best.Nic.Id), best.Nic.Speed / 1_000_000);
    }

    private static bool DnsIsStatic(string interfaceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var value = new WindowsRegistry().GetValue(RegHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceId}", "NameServer")?.Data;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static async Task<PingStats> PingAsync(string host, int count, CancellationToken ct, int timeoutMs = 1000, int intervalMs = 250)
    {
        var rtts = new List<long?>();
        using var ping = new Ping();
        for (var i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
                rtts.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch (PingException)
            {
                rtts.Add(null);
            }

            await Task.Delay(intervalMs, ct).ConfigureAwait(false);
        }

        return PingStats.From(rtts);
    }

    public static async Task<IReadOnlyList<NetworkAnalysis.SdrPop>> FetchValvePopsAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var json = await http.GetStringAsync(SdrConfigUrl, ct).ConfigureAwait(false);
        return NetworkAnalysis.ParseSdrConfig(json);
    }

    public static async Task<TrafficSample> SampleTrafficAsync(AdapterInfo adapter, TimeSpan window, CancellationToken ct)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == adapter.Name);
        if (nic is null)
        {
            return new TrafficSample(0, 0);
        }

        var a = nic.GetIPStatistics();
        await Task.Delay(window, ct).ConfigureAwait(false);
        var b = nic.GetIPStatistics();
        var secs = window.TotalSeconds;
        return new TrafficSample((b.BytesReceived - a.BytesReceived) * 8 / 1e6 / secs, (b.BytesSent - a.BytesSent) * 8 / 1e6 / secs);
    }

    public (int? Signal, string? Ssid, string? Radio) WifiInfo() => NetworkAnalysis.ParseWlan(_cmd.Run("netsh", "wlan show interfaces").StdOut);

    /// <summary>
    /// Plans a DNS change with an exact undo. DNS does not lower in-game ping; it only matters when
    /// the provider's resolver is slow or broken (Steam/store pages take long to open).
    /// </summary>
    public static PlannedAction PlanDnsChange(AdapterInfo adapter, string primary, string secondary)
    {
        var name = adapter.Name.Replace("\"", string.Empty);
        var apply = $"/c netsh interface ipv4 set dnsservers name=\"{name}\" source=static address={primary} register=primary validate=no" +
                    $" && netsh interface ipv4 add dnsservers name=\"{name}\" address={secondary} index=2 validate=no";
        string undo;
        if (!adapter.DnsIsStatic || adapter.Dns.Count == 0)
        {
            undo = $"/c netsh interface ipv4 set dnsservers name=\"{name}\" source=dhcp";
        }
        else
        {
            undo = $"/c netsh interface ipv4 set dnsservers name=\"{name}\" source=static address={adapter.Dns[0]} register=primary validate=no";
            for (var i = 1; i < adapter.Dns.Count; i++)
            {
                undo += $" && netsh interface ipv4 add dnsservers name=\"{name}\" address={adapter.Dns[i]} index={i + 1} validate=no";
            }
        }

        var before = adapter.DnsIsStatic ? string.Join(", ", adapter.Dns) : "автоматически (DHCP)";
        return PlannedAction.Run("cmd.exe", apply, "cmd.exe", undo, $"DNS адаптера «{adapter.Name}»: {before} → {primary}, {secondary}", requiresAdmin: true);
    }

    public CommandResult FlushDns() => _cmd.Run("ipconfig", "/flushdns");

    /// <summary>Winsock + TCP/IP reset. Irreversible (restores Windows defaults) and needs a reboot: UI asks for confirmation.</summary>
    public static int ResetStack() => ElevatedProcess.RunAs("cmd.exe", "/c netsh winsock reset && netsh int ip reset");
}
