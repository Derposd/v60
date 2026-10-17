using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Diagnostics;

/// <summary>Read-only checks shown in the diagnosis step. Nothing here changes the system.</summary>
public sealed partial class HealthChecks
{
    private const string UsbSubgroup = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

    private readonly ICommandRunner _cmd;

    public HealthChecks(ICommandRunner cmd)
    {
        _cmd = cmd;
    }

    [GeneratedRegex(@"0x([0-9a-f]{8})", RegexOptions.IgnoreCase)]
    private static partial Regex HexIndex();

    public IReadOnlyList<Advice> Run(SystemSnapshot s)
    {
        var list = new List<Advice>();
        Try(list, () => CheckFreeSpace(s, list));
        Try(list, () => CheckPageFile(s, list));
        Try(list, () => CheckThermal(list));
        Try(list, () => CheckUsbSuspend(list));
        Try(list, () => CheckNicPowerSaving(list));
        Try(list, () => CheckDisk(s, list));
        Try(list, () => CheckMemory(s, list));
        return list;
    }

    private static void Try(List<Advice> list, Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            Log.Warn($"Проверка не выполнена: {ex.Message}");
        }
    }

    private static void CheckFreeSpace(SystemSnapshot s, List<Advice> list)
    {
        var path = s.Steam.DotaInstallDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        if (freeGb < 10)
        {
            list.Add(new Advice(Severity.Critical, $"Мало места на диске {drive.Name}: {freeGb:0.#} ГБ",
                "Обновления Dota 2 требуют свободного места (обычно 10–20 ГБ во время установки патча), а кеш шейдеров растёт. Освободите место."));
        }
        else if (freeGb < 25)
        {
            list.Add(new Advice(Severity.Warning, $"На диске {drive.Name} осталось {freeGb:0.#} ГБ", "Крупное обновление может не установиться. Желательно иметь 25+ ГБ свободного места."));
        }

        var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        var sysFree = sys.AvailableFreeSpace / 1024d / 1024 / 1024;
        if (!string.Equals(sys.Name, drive.Name, StringComparison.OrdinalIgnoreCase) && sysFree < 10)
        {
            list.Add(new Advice(Severity.Warning, $"Мало места на системном диске {sys.Name}: {sysFree:0.#} ГБ", "Файл подкачки и кеш шейдеров драйвера живут на системном диске."));
        }
    }

    private static void CheckPageFile(SystemSnapshot s, List<Advice> list)
    {
        using var cs = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
        var auto = cs.Get().Cast<ManagementObject>().Select(o => o["AutomaticManagedPagefile"] as bool?).FirstOrDefault();
        using var pf = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage");
        var totalMb = pf.Get().Cast<ManagementObject>().Sum(o => Convert.ToInt64(o["AllocatedBaseSize"] ?? 0, CultureInfo.InvariantCulture));
        if (totalMb == 0)
        {
            list.Add(new Advice(Severity.Critical, "Файл подкачки отключён",
                "Без файла подкачки игра может вылетать при нехватке памяти (особенно при 8–16 ГБ ОЗУ). Включите: Система → О системе → Дополнительные параметры → Быстродействие → Дополнительно → Виртуальная память → «Автоматически выбирать объём»."));
        }
        else if (auto == false && totalMb < 4096 && (s.Memory?.TotalGb ?? 16) < 16)
        {
            list.Add(new Advice(Severity.Warning, $"Маленький файл подкачки ({totalMb} МБ)", "При 8 ГБ ОЗУ и меньше лучше вернуть автоматический размер файла подкачки."));
        }
    }

    private static void CheckThermal(List<Advice> list)
    {
        // ACPI thermal zones are exposed only on some machines and need admin; absence is normal.
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            var temps = s.Get().Cast<ManagementObject>()
                .Select(o => Convert.ToDouble(o["CurrentTemperature"], CultureInfo.InvariantCulture) / 10 - 273.15)
                .Where(t => t is > 0 and < 150)
                .ToList();
            if (temps.Count == 0)
            {
                list.Add(new Advice(Severity.Info, "Датчики температуры недоступны", "Windows не отдаёт температуру через стандартный интерфейс. Для контроля используйте HWiNFO или утилиту производителя."));
                return;
            }

            var max = temps.Max();
            if (max >= 90)
            {
                list.Add(new Advice(Severity.Critical, $"Высокая температура: {max:0} °C (ACPI)", "Возможен троттлинг и вылеты. Почистите систему охлаждения, на ноутбуке используйте подставку и играйте от сети, ограничьте FPS."));
            }
            else
            {
                list.Add(new Advice(Severity.Info, $"Температура (ACPI): {max:0} °C", "Это датчик материнской платы, он не всегда равен температуре процессора."));
            }
        }
        catch (ManagementException)
        {
            list.Add(new Advice(Severity.Info, "Датчики температуры недоступны", "Для контроля температуры используйте HWiNFO или утилиту производителя."));
        }
    }

    private void CheckUsbSuspend(List<Advice> list)
    {
        var r = _cmd.Run("powercfg", $"/query SCHEME_CURRENT {UsbSubgroup} {UsbSelectiveSuspend}");
        var idx = HexIndex().Matches(r.StdOut).Select(m => m.Groups[1].Value).ToList();
        // The last two hex values are the AC and DC indices.
        if (idx.Count >= 2 && Convert.ToInt32(idx[^2], 16) == 1)
        {
            list.Add(new Advice(Severity.Info, "Временное отключение USB-порта включено (от сети)",
                "Редко, но может вызывать «пропадание» мыши/гарнитуры. Если такое бывает: Панель управления → Электропитание → Настройка схемы → Изменить доп. параметры → Параметры USB → Запрещено."));
        }
    }

    private static void CheckNicPowerSaving(List<Advice> list)
    {
        using var nics = new ManagementObjectSearcher("SELECT PNPDeviceID, Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND NetEnabled = TRUE");
        var adapters = nics.Get().Cast<ManagementObject>()
            .Select(o => (Pnp: (o["PNPDeviceID"] as string ?? string.Empty).ToUpperInvariant(), Name: o["Name"] as string ?? string.Empty))
            .ToList();
        using var power = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, Enable FROM MSPower_DeviceEnable");
        foreach (var p in power.Get().Cast<ManagementObject>())
        {
            var instance = (p["InstanceName"] as string ?? string.Empty).ToUpperInvariant();
            var nic = adapters.FirstOrDefault(a => a.Pnp.Length > 0 && instance.StartsWith(a.Pnp, StringComparison.Ordinal));
            if (nic.Name is not null && p["Enable"] is true)
            {
                list.Add(new Advice(Severity.Info, $"Энергосбережение сетевой карты: {nic.Name}",
                    "Windows может отключать адаптер для экономии энергии — иногда это даёт обрывы связи. Диспетчер устройств → Сетевые адаптеры → Свойства → Управление электропитанием → снять «Разрешить отключение этого устройства»."));
            }
        }
    }

    private static void CheckDisk(SystemSnapshot s, List<Advice> list)
    {
        if (s.DotaDiskKind == DiskKind.Hdd)
        {
            list.Add(new Advice(Severity.Warning, "Dota 2 установлена на HDD",
                "Загрузка матча и подгрузка моделей будут медленнее, возможны фризы при первом появлении героев. Перенос игры на SSD — одно из самых заметных улучшений (Steam → Хранилище → Переместить)."));
        }
    }

    private static void CheckMemory(SystemSnapshot s, List<Advice> list)
    {
        if (s.Memory is null)
        {
            return;
        }

        if (s.Memory.TotalGb < 7.5)
        {
            list.Add(new Advice(Severity.Critical, $"Мало оперативной памяти: {s.Memory.TotalGb:0.#} ГБ", "Для Dota 2 минимум 8 ГБ, комфортно 16 ГБ. Закрывайте браузер во время игры."));
        }

        var igpu = s.PrimaryGpu?.IsIntegrated == true;
        if (s.Memory.LikelyDualChannel == false)
        {
            list.Add(new Advice(igpu ? Severity.Warning : Severity.Info, "Память, вероятно, работает в одноканальном режиме",
                igpu
                    ? "Установлен один модуль памяти. Для встроенной графики это критично: второй такой же модуль (двухканальный режим) по тестам обычно даёт прирост в десятки процентов FPS."
                    : "Установлен один модуль памяти. Второй такой же модуль даст прирост в процессорозависимых сценах."));
        }
    }
}
