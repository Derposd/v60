using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Gpu;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class GpuTweaksTests
{
    internal sealed class FakeDrs : INvidiaDrs
    {
        public Dictionary<uint, uint> Values { get; } = new();
        public Dictionary<uint, string> Names { get; } = new()
        {
            [GpuTweakCatalog.PreferredPState] = "Power management mode",
            [GpuTweakCatalog.PrerenderLimit] = "Maximum pre-rendered frames",
            [GpuTweakCatalog.ShaderCacheSize] = "Shader Cache - Cache Size",
        };

        public bool Available { get; set; } = true;
        public string? NameOf(uint id) => Names.TryGetValue(id, out var n) ? n : null;
        public uint? Get(uint id) => Values.TryGetValue(id, out var v) ? v : null;
        public void Set(uint id, uint value) => Values[id] = value;
        public void Reset(uint id) => Values.Remove(id);
    }

    private sealed class NoCommands : ICommandRunner
    {
        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null) => new(0, string.Empty, string.Empty);
    }

    private static readonly GpuInfo Rtx = new() { Name = "NVIDIA GeForce RTX 3060", Vendor = GpuVendor.Nvidia, VendorId = 0x10DE };
    private static readonly GpuInfo Radeon = new() { Name = "AMD Radeon RX 6600", Vendor = GpuVendor.Amd, VendorId = 0x1002 };

    private static TweakContext Ctx(params GpuInfo[] gpus) => new()
    {
        Snapshot = new SystemSnapshot { Gpus = gpus },
        Registry = new InMemoryRegistry(),
        FileSystem = new InMemoryFileSystem(),
        Commands = new NoCommands(),
    };

    private static Tweak Find(string id) => GpuTweakCatalog.All.First(t => t.Id == id);

    [Fact]
    public void Nvidia_setting_is_offered_only_on_nvidia_and_checked_by_name()
    {
        var drs = new FakeDrs();
        NvidiaSettingTweak.Drs = () => drs;
        try
        {
            Assert.Equal(TweakStatus.NotApplicable, Find("gpu.nv.power").Plan(Ctx(Radeon)).Status);

            var plan = Find("gpu.nv.power").Plan(Ctx(Rtx));
            Assert.Equal(TweakStatus.CanApply, plan.Status);
            var a = plan.Actions.Single();
            Assert.Equal(ActionKind.SetNvidiaSetting, a.Kind);
            Assert.Equal(1u, a.NvValue);
            Assert.True(a.RequiresAdmin);

            drs.Names[GpuTweakCatalog.ShaderCacheSize] = "Something else entirely";
            Assert.Equal(TweakStatus.ManualOnly, Find("gpu.nv.shadercache").Plan(Ctx(Rtx)).Status);

            drs.Values[GpuTweakCatalog.PrerenderLimit] = 1;
            Assert.Equal(TweakStatus.AlreadyOptimal, Find("gpu.nv.lowlatency").Plan(Ctx(Rtx)).Status);

            drs.Available = false;
            Assert.Equal(TweakStatus.ManualOnly, Find("gpu.nv.power").Plan(Ctx(Rtx)).Status);
        }
        finally
        {
            NvidiaSettingTweak.Drs = () => new NvidiaDrs();
        }
    }

    [Fact]
    public void Driver_setting_is_restored_or_reset_to_driver_default_on_undo()
    {
        var drs = new FakeDrs();
        var fs = new InMemoryFileSystem();
        var exec = new ChangeExecutor(fs, new InMemoryRegistry(), new NoCommands(), new BackupStore(fs, @"C:\b"), nvidia: drs);
        drs.Values[GpuTweakCatalog.PrerenderLimit] = 3;

        var r = exec.Apply("t", "nv", new[]
        {
            PlannedAction.SetNvidia(GpuTweakCatalog.PrerenderLimit, 1, "low latency"),
            PlannedAction.SetNvidia(GpuTweakCatalog.PreferredPState, 1, "power"),
        }, false);
        Assert.Equal(1u, drs.Values[GpuTweakCatalog.PrerenderLimit]);

        exec.Rollback(r.ChangeSetId!);

        Assert.Equal(3u, drs.Values[GpuTweakCatalog.PrerenderLimit]);
        Assert.False(drs.Values.ContainsKey(GpuTweakCatalog.PreferredPState));
    }

    [Fact]
    public void Vendor_panel_items_show_only_for_that_vendor()
    {
        Assert.Equal(TweakStatus.ManualOnly, Find("gpu.amd.antilag").Plan(Ctx(Radeon)).Status);
        Assert.Equal(TweakStatus.NotApplicable, Find("gpu.amd.antilag").Plan(Ctx(Rtx)).Status);
        Assert.NotNull(Find("gpu.amd.antilag").ManualSteps);
    }
}
