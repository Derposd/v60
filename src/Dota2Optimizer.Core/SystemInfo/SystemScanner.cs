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
        var cpu = Safe("процессор", ScanCpu);
        var memory = Safe("память", ScanMemory);
        var disks = Safe("диски", ScanDisks) ?? new List<DiskInfo>();
        var display = Safe("монитор", ScanDisplay);
        var windows = Safe("версия Windows", ScanWindows);
        var power = Safe("план питания", ScanPowerPlan);
        var isLaptop = Safe("тип корпуса", ScanIsLaptop);
        var steam = Safe("Steam", () => new SteamLocator(_fs, _reg).Locate(IsProcessRunning)) ?? new SteamInfo();
        var dotaDisk = steam.DotaInstallDir is null ? DiskKind.Unknown : Safe("диск с игрой", () => DiskKindForPath(steam.DotaInstallDir));
        var runtimeGpu = steam.DotaRunning ? Safe("GPU игры", () => GpuUsageProbe.FindDotaGpu(gpus)) : null;

        var snapshot = new SystemSnapshot
        {
            Gpus = gpus,
            Cpu = cpu,
            Memory = memory,
            Disks = disks,
            DotaDiskKind = dotaDisk,
            Display = display,
            Windows = windows,
            PowerPlan = power,
            Steam = steam,
            IsLaptop = isLaptop,
            DotaRuntimeGpu = runtimeGpu,
            ScanWarnings = _warnings.ToList(),
        };
        snapshot = snapshot with { Profile = ProfileClassifier.Classify(snapshot.PrimaryGpu, cpu, memory) };
        Log.Info($"Сканирование завершено: GPU={snapshot.PrimaryGpu?.Name}, профиль={snapshot.Profile}, Dota={steam.DotaInstallDir ?? "не найдена"}");
        return snapshot;
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
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException
                                       or InvalidOperationException or IOException or DllNotFoundException or EntryPointNotFoundException
                                       or PlatformNotSupportedException or TypeInitializationException)
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
        var wmi = Query("SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController").ToList();
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
        return Native.EnumDisplaySettings(null, Native.EnumCurrentSettings, ref mode)
            ? new DisplayInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, mode.dmDeviceName)
            : null;
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
