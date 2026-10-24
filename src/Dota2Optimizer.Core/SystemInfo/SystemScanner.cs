using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.SystemInfo;

/// <summary>
/// Collects the "PC passport". Every section is independent: a failing WMI class adds a warning
/// instead of aborting the scan.
/// </summary>
public sealed partial class SystemScanner
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private readonly IRegistry _reg;
    private readonly IFileSystem _fs;
    private readonly ICommandRunner _cmd;
    private readonly List<string> _warnings = new();

    public SystemScanner(IRegistry reg, IFileSystem fs, ICommandRunner cmd)
    {
        _reg = reg;
        _fs = fs;
        _cmd = cmd;
    }

    [GeneratedRegex(@"VEN_([0-9A-F]{4}).*DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PnpIds();

    [GeneratedRegex(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\s*\((.*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PowerSchemeLine();

    public SystemSnapshot Scan()
    {
        _warnings.Clear();
        Log.Info("Сканирование системы…");

        var gpus = Safe("видеокарты", ScanGpus) ?? new List<GpuInfo>();
        // Fallbacks without WMI: thread count and physical memory are always known to the runtime.
        var cpu = Safe("процессор", ScanCpu)
                  ?? new CpuInfo("Не определён", Math.Max(1, Environment.ProcessorCount / 2), Environment.ProcessorCount, 0);
        var memory = Safe("память", ScanMemory)
                     ?? new MemoryInfo((ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Array.Empty<MemoryModule>());
        var disks = Safe("диски", ScanDisks) ?? new List<DiskInfo>();
        var display = Safe("монитор", ScanDisplay);
        var windows = Safe("версия Windows", ScanWindows);
        var power = Safe("план питания", ScanPowerPlan);
        var isLaptop = Safe("тип корпуса", ScanIsLaptop);
        var games = ScanGames(gpus, null);

        var snapshot = new SystemSnapshot
        {
            Gpus = gpus,
            Cpu = cpu,
            Memory = memory,
            Disks = disks,
            Display = display,
            Windows = windows,
            PowerPlan = power,
            Games = games,
            IsLaptop = isLaptop,
            ScanWarnings = _warnings.ToList(),
        };
        snapshot = snapshot with { Profile = ProfileClassifier.Classify(snapshot.PrimaryGpu, cpu, memory) };
        Log.Info($"Сканирование завершено: GPU={snapshot.PrimaryGpu?.Name}, профиль={snapshot.Profile}, {string.Join(", ", games.Values.Select(g => $"{Games.Get(g.Game).ShortName}={g.GameInstallDir ?? "не найдена"}"))}");
        return snapshot;
    }

    /// <summary>Saved scans older than this are not reused: a full scan picks up anything the quick check misses.</summary>
    public static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(14);

    /// <summary>
    /// Quick start from a saved scan. Hardware (GPU, CPU, memory, disks, chassis) comes from the cache; things that
    /// change without new hardware (Windows, monitor mode, power plan, Steam and the games) are read again.
    /// Returns null when the cache is too old or no longer matches this PC (another video card or driver,
    /// another CPU), so the caller makes a full scan instead.
    /// </summary>
    public SystemSnapshot? Refresh(SystemSnapshot cached, DateTime now)
    {
        _warnings.Clear();
        if (now - cached.ScannedAt > CacheMaxAge || now < cached.ScannedAt || cached.Gpus.Count == 0)
        {
            return null;
        }

        try
        {
            if (!SameHardware(cached))
            {
                Log.Info("Сохранённый скан устарел: изменилось железо или драйвер видеокарты");
                return null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Проверка сохранённого скана: {ex.Message}");
            return null;
        }

        var display = Safe("монитор", ScanDisplay);
        var windows = Safe("версия Windows", ScanWindows);
        var power = Safe("план питания", ScanPowerPlan);
        var games = ScanGames(cached.Gpus, cached);

        // Keep warnings about the cached hardware sections; the re-read sections report their own.
        string[] reread = { "монитор", "версия Windows", "план питания", "Steam (", "диск с ", "GPU " };
        var warnings = cached.ScanWarnings
            .Where(w => !reread.Any(s => w.StartsWith($"Не удалось определить: {s}", StringComparison.Ordinal)))
            .Concat(_warnings)
            .ToList();

        Log.Info($"Скан от {cached.ScannedAt:dd.MM.yyyy HH:mm} использован повторно, игры и настройки Windows прочитаны заново");
        return cached with
        {
            Display = display,
            Windows = windows,
            PowerPlan = power,
            Games = games,
            ScanWarnings = warnings,
        };
    }

    /// <summary>Same video cards with the same drivers and the same thread count as in the saved scan. Cheap: no WMI.</summary>
    private bool SameHardware(SystemSnapshot cached)
    {
        if (cached.Cpu is { LogicalProcessors: > 0 } cpu && cpu.LogicalProcessors != Environment.ProcessorCount)
        {
            return false;
        }

        var now = Native.EnumerateAdapters()
            .Where(d => (d.Flags & Native.DxgiAdapterFlagSoftware) == 0)
            .Select(d => ((int)d.VendorId, (int)d.DeviceId))
            .OrderBy(x => x)
            .ToList();
        var before = cached.Gpus.Select(g => (g.VendorId, g.DeviceId)).OrderBy(x => x).ToList();
        if (now.Count > 0 && !now.SequenceEqual(before))
        {
            return false;
        }

        // The driver version in the display class key changes with every driver install.
        foreach (var g in cached.Gpus.Where(g => g.DriverVersion.Length > 0))
        {
            var current = RegistryDisplayValue(g.VendorId, g.DeviceId, "DriverVersion");
            if (current is not null && !string.Equals(current, g.DriverVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Steam and every supported game. With a cached scan the disk type is reused while the game stays in the same folder.</summary>
    private Dictionary<GameId, SteamInfo> ScanGames(IReadOnlyList<GpuInfo> gpus, SystemSnapshot? cached)
    {
        var games = new Dictionary<GameId, SteamInfo>();
        foreach (var game in Games.All)
        {
            var info = Safe($"Steam ({game.ShortName})", () => new SteamLocator(_fs, _reg).Locate(game, IsProcessRunning)) ?? new SteamInfo { Game = game.Id };
            var installDir = info.GameInstallDir;
            var old = cached?.For(game.Id);
            var disk = installDir is null
                ? DiskKind.Unknown
                : old is not null && old.DiskKind != DiskKind.Unknown && string.Equals(old.GameInstallDir, installDir, StringComparison.OrdinalIgnoreCase)
                    ? old.DiskKind
                    : Safe($"диск с {game.ShortName}", () => DiskKindForPath(installDir));
            info = info with
            {
                DiskKind = disk,
                RuntimeGpu = info.GameRunning ? Safe($"GPU {game.ShortName}", () => GpuUsageProbe.FindGameGpu(gpus, game.ProcessName)) : null,
            };
            games[game.Id] = info;
        }

        return games;
    }

    public static bool IsProcessRunning(string name)
    {
        // Only enumerates process names — no handles are opened to the game.
        var procs = Process.GetProcessesByName(name);
        foreach (var p in procs)
        {
            p.Dispose();
        }

        return procs.Length > 0;
    }

    private T? Safe<T>(string section, Func<T> f)
    {
        try
        {
            return f();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _warnings.Add($"Не удалось определить: {section} ({ex.Message})");
            Log.Warn($"Сканирование «{section}»: {ex}");
            return default;
        }
    }

    private static IEnumerable<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    private List<GpuInfo> ScanGpus()
    {
        // WMI gives driver version/date; DXGI gives exact VRAM and the LUID. Either may be unavailable on its own.
        List<ManagementObject> wmi;
        try
        {
            wmi = Query("SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController").ToList();
        }
        catch (Exception ex) when (ex is ManagementException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _warnings.Add($"WMI недоступен, версия и дата драйвера не определены ({ex.Message})");
            wmi = new List<ManagementObject>();
        }

        var dxgi = Native.EnumerateAdapters().Where(d => (d.Flags & Native.DxgiAdapterFlagSoftware) == 0).ToList();
        var result = new List<GpuInfo>();

        foreach (var d in dxgi)
        {
            var match = wmi.FirstOrDefault(w => PnpMatches(w["PNPDeviceID"] as string, (int)d.VendorId, (int)d.DeviceId));
            result.Add(BuildGpu(d.Description.Trim(), (int)d.VendorId, (int)d.DeviceId, d.DedicatedVideoMemory, d.SharedSystemMemory,
                $"0x{d.LuidHighPart:X8}_0x{d.LuidLowPart:X8}", match));
        }

        // Fallback when DXGI is unavailable (e.g. RDP session without GPU): WMI only, VRAM from registry.
        if (result.Count == 0)
        {
            foreach (var w in wmi)
            {
                var pnp = w["PNPDeviceID"] as string ?? string.Empty;
                var m = PnpIds().Match(pnp);
                var ven = m.Success ? int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
                var dev = m.Success ? int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
                var vram = RegistryVram(ven, dev) ?? Convert.ToUInt64(w["AdapterRAM"] ?? 0UL, CultureInfo.InvariantCulture);
                result.Add(BuildGpu(w["Name"] as string ?? "?", ven, dev, (nuint)vram, 0, null, w));
            }
        }

        return result;
    }

    private GpuInfo BuildGpu(string name, int ven, int dev, nuint dedicated, nuint shared, string? luid, ManagementObject? wmi)
    {
        var driverVersion = wmi?["DriverVersion"] as string ?? string.Empty;
        DateTime? driverDate = null;
        if (wmi?["DriverDate"] is string dd && dd.Length >= 8)
        {
            try
            {
                driverDate = ManagementDateTimeConverter.ToDateTime(dd);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        var vendor = GpuInfo.VendorFromId(ven);
        string? display = vendor switch
        {
            GpuVendor.Nvidia => DriverAdvisor.NvidiaMarketingVersion(driverVersion),
            GpuVendor.Intel => DriverAdvisor.IntelMarketingVersion(driverVersion),
            GpuVendor.Amd => RegistryDisplayValue(ven, dev, "RadeonSoftwareVersion"),
            _ => null,
        };

        return new GpuInfo
        {
            Name = name,
            Vendor = vendor,
            VendorId = ven,
            DeviceId = dev,
            DedicatedVideoMemory = dedicated,
            SharedSystemMemory = shared,
            IsIntegrated = ProfileClassifier.LooksIntegrated(name, ven, dedicated),
            DriverVersion = driverVersion,
            DriverDisplayVersion = display,
            DriverDate = driverDate,
            PnpDeviceId = wmi?["PNPDeviceID"] as string ?? string.Empty,
            Luid = luid,
        };
    }

    private static bool PnpMatches(string? pnp, int ven, int dev)
    {
        var m = PnpIds().Match(pnp ?? string.Empty);
        return m.Success &&
               int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) == ven &&
               int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) == dev;
    }

    private string? RegistryDisplayValue(int ven, int dev, string valueName)
    {
        foreach (var sub in _reg.GetSubKeyNames(RegHive.LocalMachine, DisplayClassKey).Where(s => s.All(char.IsDigit)))
        {
            var key = $"{DisplayClassKey}\\{sub}";
            var id = _reg.GetValue(RegHive.LocalMachine, key, "MatchingDeviceId")?.Data ?? string.Empty;
            if (id.Contains($"ven_{ven:x4}", StringComparison.OrdinalIgnoreCase) && id.Contains($"dev_{dev:x4}", StringComparison.OrdinalIgnoreCase))
            {
                return _reg.GetValue(RegHive.LocalMachine, key, valueName)?.Data;
            }
        }

        return null;
    }

    private ulong? RegistryVram(int ven, int dev) =>
        ulong.TryParse(RegistryDisplayValue(ven, dev, "HardwareInformation.qwMemorySize"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static CpuInfo? ScanCpu()
    {
        var cpu = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
        return cpu is null
            ? null
            : new CpuInfo(
                (cpu["Name"] as string ?? "?").Trim(),
                Convert.ToInt32(cpu["NumberOfCores"] ?? 0, CultureInfo.InvariantCulture),
                Convert.ToInt32(cpu["NumberOfLogicalProcessors"] ?? 0, CultureInfo.InvariantCulture),
                Convert.ToInt32(cpu["MaxClockSpeed"] ?? 0, CultureInfo.InvariantCulture));
    }

    private static MemoryInfo ScanMemory()
    {
        var total = Convert.ToUInt64(Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").First()["TotalPhysicalMemory"], CultureInfo.InvariantCulture);
        var modules = Query("SELECT Capacity, Speed, ConfiguredClockSpeed, DeviceLocator FROM Win32_PhysicalMemory")
            .Select(m => new MemoryModule(
                Convert.ToUInt64(m["Capacity"] ?? 0UL, CultureInfo.InvariantCulture),
                Convert.ToInt32(m["ConfiguredClockSpeed"] ?? m["Speed"] ?? 0, CultureInfo.InvariantCulture),
                m["DeviceLocator"] as string ?? string.Empty))
            .ToList();
        return new MemoryInfo(total, modules);
    }

    private static List<DiskInfo> ScanDisks() =>
        Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
            .Select(d => new DiskInfo(
                d["FriendlyName"] as string ?? "?",
                DiskKindOf(Convert.ToInt32(d["MediaType"] ?? 0, CultureInfo.InvariantCulture), Convert.ToInt32(d["BusType"] ?? 0, CultureInfo.InvariantCulture)),
                Convert.ToUInt64(d["Size"] ?? 0UL, CultureInfo.InvariantCulture)))
            .ToList();

    private static DiskKind DiskKindOf(int mediaType, int busType) => (mediaType, busType) switch
    {
        (_, 17) => DiskKind.Nvme,
        (4, _) => DiskKind.Ssd,
        (3, _) => DiskKind.Hdd,
        _ => DiskKind.Unknown,
    };

    private static DiskKind DiskKindForPath(string path)
    {
        var letter = Path.GetPathRoot(path)?.TrimEnd('\\', ':');
        if (string.IsNullOrEmpty(letter) || letter.Length != 1)
        {
            return DiskKind.Unknown;
        }

        const string scope = @"root\Microsoft\Windows\Storage";
        var partition = Query($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'", scope).FirstOrDefault();
        if (partition is null)
        {
            return DiskKind.Unknown;
        }

        var number = Convert.ToString(partition["DiskNumber"], CultureInfo.InvariantCulture);
        var disk = Query($"SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '{number}'", scope).FirstOrDefault();
        return disk is null
            ? DiskKind.Unknown
            : DiskKindOf(Convert.ToInt32(disk["MediaType"] ?? 0, CultureInfo.InvariantCulture), Convert.ToInt32(disk["BusType"] ?? 0, CultureInfo.InvariantCulture));
    }

    private static DisplayInfo? ScanDisplay()
    {
        var mode = new Native.DevMode { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DevMode>() };
        if (!Native.EnumDisplaySettings(null, Native.EnumCurrentSettings, ref mode))
        {
            return null;
        }

        return new DisplayInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 0, mode.dmDeviceName)
        {
            MaxRefreshHz = MaxRefreshAt(mode.dmDeviceName, mode.dmPelsWidth, mode.dmPelsHeight),
        };
    }

    /// <summary>Highest refresh rate among the display's modes with the same resolution. Read-only: no mode is ever set.</summary>
    private static int MaxRefreshAt(string deviceName, int width, int height)
    {
        var max = 0;
        var probe = new Native.DevMode { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DevMode>() };
        // A display has a few hundred modes at most; the cap only guards against a driver that never says "no more".
        for (var i = 0; i < 4096 && Native.EnumDisplaySettings(deviceName, i, ref probe); i++)
        {
            if (probe.dmPelsWidth == width && probe.dmPelsHeight == height && probe.dmDisplayFrequency > max)
            {
                max = probe.dmDisplayFrequency;
            }
        }

        return max > 1 ? max : 0;
    }

    private WindowsInfo ScanWindows()
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var build = int.TryParse(_reg.GetValue(RegHive.LocalMachine, key, "CurrentBuildNumber")?.Data, out var b) ? b : Environment.OSVersion.Version.Build;
        var product = _reg.GetValue(RegHive.LocalMachine, key, "ProductName")?.Data ?? "Windows";
        var isWin11 = build >= 22000;
        if (isWin11)
        {
            // ProductName still says "Windows 10" on Windows 11 for compatibility reasons.
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var display = _reg.GetValue(RegHive.LocalMachine, key, "DisplayVersion")?.Data ?? string.Empty;
        return new WindowsInfo(product, display, build, isWin11);
    }

    private PowerPlanInfo? ScanPowerPlan()
    {
        var active = _cmd.Run("powercfg", "/getactivescheme");
        var list = _cmd.Run("powercfg", "/list");
        var m = PowerSchemeLine().Match(active.StdOut);
        if (!m.Success)
        {
            return null;
        }

        var available = PowerSchemeLine().Matches(list.StdOut)
            .Select(x => (Guid.Parse(x.Groups[1].Value), x.Groups[2].Value.Trim()))
            .ToList();
        return new PowerPlanInfo(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), available);
    }

    private static bool ScanIsLaptop()
    {
        if (Query("SELECT Name FROM Win32_Battery").Any())
        {
            return true;
        }

        var portable = new HashSet<int> { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
        return Query("SELECT ChassisTypes FROM Win32_SystemEnclosure")
            .SelectMany(e => e["ChassisTypes"] as ushort[] ?? Array.Empty<ushort>())
            .Any(t => portable.Contains(t));
    }
}
