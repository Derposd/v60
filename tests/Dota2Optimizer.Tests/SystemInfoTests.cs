using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Tests;

public class SystemInfoTests
{
    private static GpuInfo Gpu(string name, int ven, double vramGb) => new()
    {
        Name = name,
        VendorId = ven,
        Vendor = GpuInfo.VendorFromId(ven),
        DedicatedVideoMemory = (ulong)(vramGb * 1024 * 1024 * 1024),
        IsIntegrated = ProfileClassifier.LooksIntegrated(name, ven, (ulong)(vramGb * 1024 * 1024 * 1024)),
    };

    private static readonly CpuInfo Cpu8 = new("Ryzen 7", 8, 16, 4500);
    private static readonly MemoryInfo Ram32 = new(32UL << 30, new[] { new MemoryModule(16UL << 30, 6000, "A"), new MemoryModule(16UL << 30, 6000, "B") });
    private static readonly MemoryInfo Ram8Single = new(8UL << 30, new[] { new MemoryModule(8UL << 30, 3200, "A") });

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", 0x10DE, 12, PcProfile.Strong)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", 0x10DE, 6, PcProfile.Medium)]
    [InlineData("NVIDIA GeForce GTX 1050 Ti", 0x10DE, 4, PcProfile.Weak)]
    [InlineData("NVIDIA GeForce GTX 970", 0x10DE, 4, PcProfile.Weak)]
    [InlineData("NVIDIA GeForce GTX 470", 0x10DE, 16, PcProfile.Weak)]
    [InlineData("NVIDIA GeForce GTX 1080", 0x10DE, 8, PcProfile.Medium)]
    [InlineData("AMD Radeon RX 7800 XT", 0x1002, 16, PcProfile.Strong)]
    [InlineData("AMD Radeon RX 580", 0x1002, 8, PcProfile.Medium)]
    [InlineData("Intel(R) UHD Graphics 620", 0x8086, 0.125, PcProfile.Weak)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", 0x8086, 12, PcProfile.Strong)]
    public void Classifies_gpus(string name, int ven, double vram, PcProfile expected)
    {
        Assert.Equal(expected, ProfileClassifier.Classify(Gpu(name, ven, vram), Cpu8, Ram32));
    }

    [Fact]
    public void Low_ram_forces_weak_profile()
    {
        Assert.Equal(PcProfile.Weak, ProfileClassifier.Classify(Gpu("NVIDIA GeForce RTX 4070", 0x10DE, 12), Cpu8, new MemoryInfo(4UL << 30, Array.Empty<MemoryModule>())));
    }

    [Fact]
    public void Strong_igpu_needs_dual_channel_memory()
    {
        var igpu = Gpu("AMD Radeon 780M Graphics", 0x1002, 0.5);
        Assert.True(igpu.IsIntegrated);
        Assert.Equal(PcProfile.Medium, ProfileClassifier.Classify(igpu, Cpu8, Ram32));
        Assert.Equal(PcProfile.Weak, ProfileClassifier.Classify(igpu, Cpu8, Ram8Single));
    }

    [Theory]
    [InlineData("AMD Radeon(TM) Graphics", 0x1002, 0.5, true)]
    [InlineData("AMD Radeon RX 6600", 0x1002, 8, false)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", 0x8086, 0.125, true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", 0x8086, 16, false)]
    [InlineData("Intel(R) Arc(TM) 140V GPU", 0x8086, 0.125, true)]
    [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", 0x10DE, 6, false)]
    public void Detects_integrated(string name, int ven, double vram, bool integrated)
    {
        Assert.Equal(integrated, ProfileClassifier.LooksIntegrated(name, ven, (ulong)(vram * 1024 * 1024 * 1024)));
    }

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("32.0.15.8129", "581.29")]
    [InlineData("bad", null)]
    public void Nvidia_marketing_version(string windows, string? expected)
    {
        Assert.Equal(expected, DriverAdvisor.NvidiaMarketingVersion(windows));
    }

    [Fact]
    public void Old_driver_is_reported()
    {
        var gpu = Gpu("NVIDIA GeForce RTX 3060", 0x10DE, 12) with { DriverVersion = "31.0.15.3623", DriverDate = new DateTime(2023, 6, 1) };
        var advice = DriverAdvisor.Check(gpu, new WindowsInfo("Windows 11 Pro", "24H2", 26100, true), new DateTime(2026, 10, 17));
        Assert.Contains(advice, a => a.Severity == Severity.Critical && a.Link == DriverAdvisor.NvidiaDownload);
    }

    [Fact]
    public void Windows10_warning_is_shown()
    {
        var gpu = Gpu("NVIDIA GeForce RTX 3060", 0x10DE, 12) with { DriverVersion = "32.0.15.8129", DriverDate = new DateTime(2026, 9, 1) };
        var advice = DriverAdvisor.Check(gpu, new WindowsInfo("Windows 10 Pro", "22H2", 19045, false), new DateTime(2026, 10, 17));
        Assert.Single(advice);
        Assert.Contains("Windows 10", advice[0].Title);
    }

    [Fact]
    public void Hybrid_detection_and_primary_gpu()
    {
        var s = new SystemSnapshot
        {
            Gpus = new[] { Gpu("Intel(R) UHD Graphics", 0x8086, 0.125), Gpu("NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 8) },
        };
        Assert.True(s.IsHybridGraphics);
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU", s.PrimaryGpu!.Name);
    }

    [Fact]
    public void Dual_channel_guess()
    {
        Assert.False(Ram8Single.LikelyDualChannel);
        Assert.True(Ram32.LikelyDualChannel);
        Assert.Null(new MemoryInfo(0, Array.Empty<MemoryModule>()).LikelyDualChannel);
    }
}
