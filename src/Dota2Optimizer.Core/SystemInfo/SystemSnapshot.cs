namespace Dota2Optimizer.Core.SystemInfo;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
}

public enum PcProfile
{
    Weak,
    Medium,
    Strong,
}

public enum DiskKind
{
    Unknown,
    Hdd,
    Ssd,
    Nvme,
}

public sealed record GpuInfo
{
    public string Name { get; init; } = string.Empty;
    public GpuVendor Vendor { get; init; }
    public int VendorId { get; init; }
    public int DeviceId { get; init; }
    public ulong DedicatedVideoMemory { get; init; }
    public ulong SharedSystemMemory { get; init; }
    public bool IsIntegrated { get; init; }
    public string DriverVersion { get; init; } = string.Empty;

    /// <summary>Marketing version where it can be derived (NVIDIA 566.36, AMD Adrenalin 24.x).</summary>
    public string? DriverDisplayVersion { get; init; }

    public DateTime? DriverDate { get; init; }
    public string PnpDeviceId { get; init; } = string.Empty;

    /// <summary>DXGI adapter LUID as "0xHIGH_0xLOW" (matches the "GPU Engine" counter instance names).</summary>
    public string? Luid { get; init; }

    public double VramGb => DedicatedVideoMemory / 1024d / 1024d / 1024d;

    public static GpuVendor VendorFromId(int vendorId) => vendorId switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 or 0x1022 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        0x1414 => GpuVendor.Microsoft, // Basic Render Driver / Remote Display
        _ => GpuVendor.Unknown,
    };
}

public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors, int MaxClockMhz);

public sealed record MemoryModule(ulong CapacityBytes, int SpeedMhz, string Slot);

public sealed record MemoryInfo(ulong TotalBytes, IReadOnlyList<MemoryModule> Modules)
{
    public double TotalGb => TotalBytes / 1024d / 1024d / 1024d;

    /// <summary>
    /// Rough dual-channel guess: one module almost always means single channel.
    /// Two or more modules usually means dual channel but this cannot be proven from WMI.
    /// </summary>
    public bool? LikelyDualChannel => Modules.Count switch
    {
        0 => null,
        1 => false,
        _ => true,
    };
}

public sealed record DiskInfo(string Model, DiskKind Kind, ulong SizeBytes);

public sealed record DisplayInfo(int Width, int Height, int RefreshHz, string DeviceName);

public sealed record WindowsInfo(string ProductName, string DisplayVersion, int Build, bool IsWindows11);

public sealed record PowerPlanInfo(Guid ActiveGuid, string ActiveName, IReadOnlyList<(Guid Guid, string Name)> Available);

/// <summary>Steam and one game's install. Each supported game gets its own instance in <see cref="SystemSnapshot.Games"/>.</summary>
public sealed record SteamInfo
{
    public Game.GameId Game { get; init; }
    public string? SteamPath { get; init; }
    public IReadOnlyList<string> Libraries { get; init; } = Array.Empty<string>();
    public string? GameInstallDir { get; init; }
    public string? GameExe { get; init; }
    public string? GameCfgDir { get; init; }
    public IReadOnlyList<string> VideoConfigPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CloudCfgDirs { get; init; } = Array.Empty<string>();
    public string? ActiveAccountId { get; init; }
    public string? LocalConfigPath { get; init; }
    public bool SteamRunning { get; init; }
    public bool GameRunning { get; init; }
    public string? GameBuildId { get; init; }

    /// <summary>Kind of disk the game is installed on.</summary>
    public DiskKind DiskKind { get; init; }

    /// <summary>Name of the GPU the game is rendering on right now (only known while it runs).</summary>
    public string? RuntimeGpu { get; init; }

    public bool GameFound => GameInstallDir is not null;
}

public sealed record SystemSnapshot
{
    public DateTime ScannedAt { get; init; } = DateTime.Now;
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = Array.Empty<GpuInfo>();
    public CpuInfo? Cpu { get; init; }
    public MemoryInfo? Memory { get; init; }
    public IReadOnlyList<DiskInfo> Disks { get; init; } = Array.Empty<DiskInfo>();
    public DisplayInfo? Display { get; init; }
    public WindowsInfo? Windows { get; init; }
    public PowerPlanInfo? PowerPlan { get; init; }
    /// <summary>Steam data per supported game.</summary>
    public IReadOnlyDictionary<Game.GameId, SteamInfo> Games { get; init; } = new Dictionary<Game.GameId, SteamInfo>();
    public bool IsLaptop { get; init; }

    public PcProfile Profile { get; init; }
    public IReadOnlyList<string> ScanWarnings { get; init; } = Array.Empty<string>();

    public IEnumerable<GpuInfo> RealGpus => Gpus.Where(g => g.Vendor is not GpuVendor.Microsoft and not GpuVendor.Unknown);

    public SteamInfo For(Game.GameId game) => Games.TryGetValue(game, out var info) ? info : new SteamInfo { Game = game };

    /// <summary>The GPU games should use: the discrete card if present, otherwise the best integrated one.</summary>
    public GpuInfo? PrimaryGpu =>
        RealGpus.OrderBy(g => g.IsIntegrated).ThenByDescending(g => g.DedicatedVideoMemory).FirstOrDefault();

    public bool IsHybridGraphics => RealGpus.Any(g => g.IsIntegrated) && RealGpus.Any(g => !g.IsIntegrated);
}
