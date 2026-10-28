using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Network;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class NetworkTweaksTests
{
    private sealed class Netsh : ICommandRunner
    {
        public string Output { get; set; } = string.Empty;

        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null) => new(0, Output, string.Empty);
    }

    private const string Key = NetAdapters.ClassKey + @"\0001";
    private readonly InMemoryRegistry _reg = new();

    private void AddAdapter(bool wifi = false)
    {
        _reg.SetValue(RegHive.LocalMachine, Key, "DriverDesc", RegValue.Str(wifi ? "Intel(R) Wi-Fi 6 AX201" : "Realtek PCIe GbE Family Controller"));
        _reg.SetValue(RegHive.LocalMachine, Key, "Characteristics", RegValue.Dword(0x84));
        _reg.SetValue(RegHive.LocalMachine, Key, "*IfType", RegValue.Dword(wifi ? 71 : 6));
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\*InterruptModeration", "default", RegValue.Str("1"));
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\*InterruptModeration\enum", "0", RegValue.Str("Disabled"));
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\*InterruptModeration\enum", "1", RegValue.Str("Enabled"));
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\*ReceiveBuffers", "default", RegValue.Str("512"));
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\*ReceiveBuffers", "max", RegValue.Str("1024"));
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\*EEE", "default", RegValue.Str("1"));
        // A virtual adapter (Hyper-V) is ignored.
        _reg.SetValue(RegHive.LocalMachine, NetAdapters.ClassKey + @"\0002", "DriverDesc", RegValue.Str("Hyper-V Virtual Ethernet Adapter"));
        _reg.SetValue(RegHive.LocalMachine, NetAdapters.ClassKey + @"\0002", "Characteristics", RegValue.Dword(0x1));
    }

    private TweakContext Ctx(string netsh = "", int build = 26100) => new()
    {
        Snapshot = new SystemSnapshot { Windows = new WindowsInfo("Windows 11", "24H2", build, true) },
        Registry = _reg,
        FileSystem = new InMemoryFileSystem(),
        Commands = new Netsh { Output = netsh },
    };

    [Fact]
    public void Only_physical_adapters_and_properties_the_driver_declares_are_offered()
    {
        AddAdapter();
        var adapter = Assert.Single(NetAdapters.Physical(_reg));
        Assert.False(adapter.IsWifi);

        var ids = AdapterPropertyTweak.For(adapter).Select(t => t.Setting.Id).ToList();
        Assert.Equal(new[] { "irqmod", "eee", "rxbuf" }, ids);
    }

    [Fact]
    public void Adapter_property_is_set_and_the_card_restarts_after_apply_and_after_undo()
    {
        AddAdapter();
        var tweak = AdapterPropertyTweak.For(NetAdapters.Physical(_reg)[0]).First(t => t.Setting.Id == "rxbuf");

        var plan = tweak.Plan(Ctx());

        Assert.Equal(TweakStatus.CanApply, plan.Status);
        Assert.Equal(3, plan.Actions.Count);
        Assert.Equal("powershell.exe", plan.Actions[0].UndoCommand);
        Assert.Equal("1024", plan.Actions[1].Value!.Data);
        Assert.Equal("powershell.exe", plan.Actions[2].Command);
        Assert.Contains("Restart-NetAdapter -InterfaceDescription 'Realtek PCIe GbE Family Controller'", plan.Actions[2].Arguments);
        Assert.True(plan.RequiresAdmin);

        _reg.SetValue(RegHive.LocalMachine, Key, "*ReceiveBuffers", RegValue.Str("1024"));
        Assert.Equal(TweakStatus.AlreadyOptimal, tweak.Plan(Ctx()).Status);
    }

    [Fact]
    public void Quotes_in_the_adapter_name_cannot_break_the_command() =>
        Assert.Equal("-NoProfile -NonInteractive -Command \"Restart-NetAdapter -InterfaceDescription 'Bob''s NIC' -Confirm:$false\"",
            NetAdapters.RestartArgs(new NetAdapter("k", "Bob's \"NIC", false)));

    [Fact]
    public void Wifi_gets_roaming_but_not_energy_efficient_ethernet()
    {
        AddAdapter(wifi: true);
        _reg.SetValue(RegHive.LocalMachine, Key + @"\Ndi\Params\RoamAggressiveness", "default", RegValue.Str("2"));
        var ids = AdapterPropertyTweak.For(NetAdapters.Physical(_reg)[0]).Select(t => t.Setting.Id).ToList();

        Assert.Contains("roam", ids);
        Assert.DoesNotContain("eee", ids);
    }

    [Theory]
    [InlineData("Receive Window Auto-Tuning Level    : disabled\r\nReceive-Side Scaling State : enabled", "disabled")]
    [InlineData("Уровень автонастройки окна получения    : normal", "normal")]
    [InlineData("nothing", null)]
    public void Autotuning_level_is_read_in_any_language(string output, string? level) => Assert.Equal(level, AutotuningTweak.Parse(output));

    [Fact]
    public void Autotuning_is_offered_only_when_someone_changed_it()
    {
        Assert.Equal(TweakStatus.AlreadyOptimal, new AutotuningTweak().Plan(Ctx("Receive Window Auto-Tuning Level : normal")).Status);
        var plan = new AutotuningTweak().Plan(Ctx("Receive Window Auto-Tuning Level : highlyrestricted"));
        Assert.Equal("int tcp set global autotuninglevel=highlyrestricted", plan.Actions.Single().UndoArguments);
    }

    [Fact]
    public void Congestion_provider_needs_windows_11_22h2_and_undoes_to_the_previous_one()
    {
        const string output = "TCP Supplemental Parameters\r\nMinimum RTO (msec) : 300\r\nCongestion Control Provider : cubic";
        Assert.Equal(TweakStatus.NotApplicable, new CongestionTweak().Plan(Ctx(output, build: 19045)).Status);
        var plan = new CongestionTweak().Plan(Ctx(output));
        Assert.EndsWith("congestionprovider=cubic", plan.Actions.Single().UndoArguments);
        Assert.True(new CongestionTweak().Disputed);
    }

    [Fact]
    public void Dscp_policy_is_a_registry_key_with_dscp_46_for_the_exe()
    {
        var plan = new DscpTweak("cs2.exe", "CS2").Plan(Ctx());

        Assert.Contains(plan.Actions, a => a.ValueName == "Application Name" && a.Value!.Data == "cs2.exe");
        Assert.Contains(plan.Actions, a => a.ValueName == "DSCP Value" && a.Value!.Data == "46");
        Assert.Contains(plan.Actions, a => a.ValueName == "Do not use NLA");
        Assert.Equal("gpupdate.exe", plan.Actions[^1].Command);
        Assert.Equal(TweakCategory.Network, new DscpTweak("cs2.exe", "CS2").Category);
    }

    [Fact]
    public void Dns_query_packet_is_standard()
    {
        var q = DnsBenchmark.BuildQuery(0x1234, "google.com");

        Assert.Equal(new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 6 }, q[..13]);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 1 }, q[^5..]);
        Assert.True(DnsBenchmark.IsAnswer(new byte[] { 0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 }, 0x1234));
        Assert.False(DnsBenchmark.IsAnswer(new byte[] { 0x12, 0x35, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 }, 0x1234));
        Assert.False(DnsBenchmark.IsAnswer(new byte[] { 0x12, 0x34, 0x81, 0x82, 0, 1, 0, 0, 0, 0, 0, 0 }, 0x1234));
    }

    [Fact]
    public void Median_of_replies()
    {
        Assert.Equal(20, DnsBenchmark.Median(new double[] { 30, 10, 20 }));
        Assert.Equal(15, DnsBenchmark.Median(new double[] { 10, 20 }));
        Assert.Null(DnsBenchmark.Median(Array.Empty<double>()));
    }
}
