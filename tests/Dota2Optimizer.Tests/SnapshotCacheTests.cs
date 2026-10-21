using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Tests;

public class SnapshotCacheTests
{
    private const string Path = @"C:\data\last-scan.json";

    private static SystemSnapshot Sample() => new()
    {
        ScannedAt = new DateTime(2026, 10, 20, 18, 30, 0),
        Gpus = new[]
        {
            new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = GpuVendor.Nvidia, VendorId = 0x10DE, DeviceId = 0x2503, DedicatedVideoMemory = 12UL << 30, DriverVersion = "32.0.15.6094", DriverDisplayVersion = "560.94", DriverDate = new DateTime(2024, 8, 1), Luid = "0x00000000_0x0000C3B1" },
            new GpuInfo { Name = "Intel(R) UHD Graphics 770", Vendor = GpuVendor.Intel, VendorId = 0x8086, DeviceId = 0x4680, DedicatedVideoMemory = 128UL << 20, IsIntegrated = true },
        },
        Cpu = new CpuInfo("Intel Core i5-12400F", 6, 12, 2500),
        Memory = new MemoryInfo(16UL << 30, new[] { new MemoryModule(8UL << 30, 3200, "DIMM1"), new MemoryModule(8UL << 30, 3200, "DIMM2") }),
        Disks = new[] { new DiskInfo("Samsung 980", DiskKind.Nvme, 1UL << 40) },
        Display = new DisplayInfo(1920, 1080, 144, @"\\.\DISPLAY1"),
        Windows = new WindowsInfo("Windows 11 Pro", "24H2", 26100, true),
        PowerPlan = new PowerPlanInfo(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Сбалансированная",
            new[] { (Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Сбалансированная"), (Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "Высокая производительность") }),
        Games = new Dictionary<GameId, SteamInfo>
        {
            [GameId.Dota2] = new() { Game = GameId.Dota2, SteamPath = @"C:\Steam", GameInstallDir = @"D:\Steam\steamapps\common\dota 2 beta", DiskKind = DiskKind.Ssd, VideoConfigPaths = new[] { @"D:\video.txt" } },
            [GameId.Cs2] = new() { Game = GameId.Cs2, SteamPath = @"C:\Steam" },
        },
        Profile = PcProfile.Medium,
        ScanWarnings = new[] { "Не удалось определить: тип корпуса (x)" },
    };

    [Fact]
    public void Saved_scan_reads_back_the_same()
    {
        var fs = new InMemoryFileSystem();
        var cache = new SnapshotCache(fs, Path);
        var original = Sample();

        cache.Save(original);
        var loaded = cache.Load();

        Assert.NotNull(loaded);
        Assert.Equal(original.ScannedAt, loaded!.ScannedAt);
        Assert.Equal(original.Gpus, loaded.Gpus);
        Assert.Equal("NVIDIA GeForce RTX 3060", loaded.PrimaryGpu?.Name);
        Assert.True(loaded.IsHybridGraphics);
        Assert.Equal(original.Cpu, loaded.Cpu);
        Assert.Equal(original.Memory!.TotalBytes, loaded.Memory!.TotalBytes);
        Assert.Equal(original.Memory.Modules, loaded.Memory.Modules);
        Assert.Equal(original.Disks, loaded.Disks);
        Assert.Equal(original.Display, loaded.Display);
        Assert.Equal(original.Windows, loaded.Windows);
        Assert.Equal(original.PowerPlan!.ActiveGuid, loaded.PowerPlan!.ActiveGuid);
        Assert.Equal(original.PowerPlan.Available, loaded.PowerPlan.Available);
        Assert.Equal(PcProfile.Medium, loaded.Profile);
        Assert.Equal(original.ScanWarnings, loaded.ScanWarnings);

        var dota = loaded.For(GameId.Dota2);
        Assert.True(dota.GameFound);
        Assert.Equal(DiskKind.Ssd, dota.DiskKind);
        Assert.Equal(original.For(GameId.Dota2).VideoConfigPaths, dota.VideoConfigPaths);
        Assert.False(loaded.For(GameId.Cs2).GameFound);
    }

    [Fact]
    public void Missing_or_broken_file_means_no_cache()
    {
        var fs = new InMemoryFileSystem();
        var cache = new SnapshotCache(fs, Path);
        Assert.Null(cache.Load());

        fs.WriteAllText(Path, "{ not json");
        Assert.Null(cache.Load());
    }

    [Fact]
    public void File_from_another_format_version_is_ignored()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText(Path, """{ "Version": 999, "Snapshot": { "Profile": 2 } }""");
        Assert.Null(new SnapshotCache(fs, Path).Load());
    }
}
