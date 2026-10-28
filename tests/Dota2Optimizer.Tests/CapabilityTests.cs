using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Upscale;

namespace Dota2Optimizer.Tests;

public class CapabilityTests
{
    private static IReadOnlyDictionary<string, Capability> For(string name, GpuVendor vendor, int level, bool dx12, bool integrated = false) =>
        CapabilityMatrix.For(new GpuInfo { Name = name, Vendor = vendor, IsIntegrated = integrated }, new GpuCaps(name, level, dx12), 26100).ToDictionary(c => c.Id);

    [Fact]
    public void Old_fermi_card_gets_the_honest_picture()
    {
        var c = For("NVIDIA GeForce GTX 470", GpuVendor.Nvidia, 0xB000, dx12: false);

        Assert.Equal(CapStatus.Experimental, c["magpie"].Status);
        Assert.Equal(CapStatus.NotAvailable, c["dlss"].Status);
        Assert.Equal(CapStatus.NotAvailable, c["dlssfg"].Status);
        Assert.Equal(CapStatus.NotAvailable, c["nis"].Status);
        Assert.Equal(CapStatus.NotAvailable, c["fsr3fg"].Status);
        Assert.Equal(CapStatus.NotAvailable, c["optiscaler"].Status);
    }

    [Fact]
    public void Card_without_feature_level_11_is_told_so()
    {
        var c = For("ATI Radeon HD 4850", GpuVendor.Amd, 0xA100, dx12: false);
        Assert.Equal(CapStatus.NotAvailable, c["magpie"].Status);
        Assert.Contains("11_0", c["magpie"].Why);
        Assert.Equal(CapStatus.NotAvailable, c["lossless"].Status);
    }

    [Fact]
    public void Rtx_40_has_frame_generation_rtx_30_does_not()
    {
        Assert.Equal(CapStatus.Works, For("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, 0xC200, true)["dlssfg"].Status);
        var ampere = For("NVIDIA GeForce RTX 3060 Laptop GPU", GpuVendor.Nvidia, 0xC200, true);
        Assert.Equal(CapStatus.NotAvailable, ampere["dlssfg"].Status);
        Assert.Equal(CapStatus.Works, ampere["dlss"].Status);
        Assert.Equal(CapStatus.Works, ampere["fsr3fg"].Status);
        Assert.Contains("Multi", For("NVIDIA GeForce RTX 5080", GpuVendor.Nvidia, 0xC200, true)["dlssfg"].Name);
    }

    [Theory]
    [InlineData("AMD Radeon RX 6600", CapStatus.Works, CapStatus.Works)]
    [InlineData("AMD Radeon RX 5700 XT", CapStatus.NotAvailable, CapStatus.Works)]
    [InlineData("Radeon RX 580 Series", CapStatus.NotAvailable, CapStatus.NotAvailable)]
    [InlineData("AMD Radeon 780M Graphics", CapStatus.Works, CapStatus.Works)]
    public void Amd_driver_features_follow_the_rdna_generation(string name, CapStatus afmf, CapStatus rsr)
    {
        var c = For(name, GpuVendor.Amd, 0xC100, true);
        Assert.Equal(afmf, c["afmf"].Status);
        Assert.Equal(rsr, c["rsr"].Status);
    }

    [Fact]
    public void Feature_level_text()
    {
        Assert.Equal("11_0", new GpuCaps("x", 0xB000, false).FeatureLevelText);
        Assert.Equal("12_1", new GpuCaps("x", 0xC100, true).FeatureLevelText);
        Assert.Equal("не определён", new GpuCaps("x", 0, false).FeatureLevelText);
    }
}
