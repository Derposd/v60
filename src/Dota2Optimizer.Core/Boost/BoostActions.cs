using System.Runtime.InteropServices;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Boost;

public sealed record BoostResult(bool Success, string Message);

/// <summary>A one-off action ("Boost-Up"): nothing to undo, so each one says plainly what it does and how much it helps.</summary>
public sealed record BoostAction(string Id, string Title, string Description, Effectiveness Effect, bool RequiresAdmin, string Honest);

public static class BoostCatalog
{
    public const string Temp = "boost.temp";
    public const string Dns = "boost.dns";
    public const string Standby = "boost.standby";
    public const string Explorer = "boost.explorer";
    public const string Trim = "boost.trim";
    public const string Shaders = "boost.shaders";

    public static IReadOnlyList<BoostAction> All { get; } = new[]
    {
        new BoostAction(Temp, "Очистить временные файлы", "Файлы из TEMP старше суток. Используемые файлы пропускаются.", Effectiveness.Low, false,
            "На FPS не влияет: освобождает место на диске. Полезно, если на системном диске меньше 10–15% свободного места."),
        new BoostAction(Dns, "Сбросить кэш DNS", "ipconfig /flushdns.", Effectiveness.Low, false,
            "Помогает, только если сайт или сервер лаунчера переехал и не открывается. На пинг не влияет."),
        new BoostAction(Shaders, "Очистить кэш шейдеров", "DirectX и кэш драйвера вашей видеокарты (NVIDIA / AMD / Intel).", Effectiveness.HardwareDependent, false,
            "Помогает при фризах и артефактах после обновления драйвера. Первый запуск игр после очистки будет с подтормаживаниями, пока шейдеры не соберутся заново."),
        new BoostAction(Explorer, "Перезапустить Проводник", "Закрывает и снова запускает explorer.exe (панель задач и рабочий стол мигнут).", Effectiveness.Low, false,
            "Помогает, если панель задач или «Пуск» зависли или Проводник разросся по памяти. Открытые окна Проводника закроются."),
        new BoostAction(Trim, "TRIM для SSD", "Оптимизация дисков Windows: команда ReTrim для всех SSD (defrag /C /L).", Effectiveness.Low, true,
            "Windows и так делает это раз в неделю. Имеет смысл, если плановая оптимизация отключена или SSD почти заполнен. Жёсткие диски не трогаются."),
        new BoostAction(Standby, "Очистить резервный список памяти (standby)", "Сбрасывает кэш файлов в оперативной памяти, как утилита RAMMap.", Effectiveness.Low, true,
            "Честно: обычно не нужно. Windows сама отдаёт эту память игре по требованию, а после очистки файлы будут читаться с диска заново. " +
            "Помогает в редком случае, когда кэш не освобождается (известная проблема отдельных сборок Windows 10) и игра упирается в нехватку памяти."),
    };
}

/// <summary>Empties the standby page list (NtSetSystemInformation, the same call RAMMap uses). Needs administrator rights.</summary>
public static class MemoryTools
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint TokenAdjustPrivileges = 0x20;
    private const uint TokenQuery = 0x8;
    private const uint PrivilegeEnabled = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr prev, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    public static BoostResult PurgeStandbyList()
    {
        if (!Elevation.IsElevated)
        {
            return new BoostResult(false, "Нужны права администратора.");
        }

        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return new BoostResult(false, $"Не удалось открыть токен процесса (код {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
            {
                return new BoostResult(false, "Нет привилегии SeProfileSingleProcessPrivilege.");
            }

            var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
            AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf<TokenPrivileges>(), IntPtr.Zero, IntPtr.Zero);
            var command = MemoryPurgeStandbyList;
            var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            Log.Info($"Очистка standby list: NTSTATUS 0x{status:X8}");
            return status == 0
                ? new BoostResult(true, "Резервный список памяти очищен.")
                : new BoostResult(false, $"Windows отказала (NTSTATUS 0x{status:X8}).");
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
