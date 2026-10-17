using System.Management;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

public sealed record RestorePointResult(bool Success, string Message);

/// <summary>System Restore point via the documented WMI SystemRestore class. Requires administrator rights.</summary>
public static class RestorePoint
{
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;

    public static RestorePointResult Create(string description)
    {
        if (!Elevation.IsElevated)
        {
            return new RestorePointResult(false, "Нужны права администратора");
        }

        try
        {
            using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var input = cls.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = ModifySettings;
            input["EventType"] = BeginSystemChange;
            using var output = cls.InvokeMethod("CreateRestorePoint", input, null);
            var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);
            if (code == 0)
            {
                Log.Info($"Точка восстановления создана: {description}");
                return new RestorePointResult(true,
                    "Точка восстановления запрошена. Windows создаёт не больше одной точки в сутки — если точка уже была сегодня, новая может не появиться (наши собственные копии файлов и реестра делаются всегда).");
            }

            return new RestorePointResult(false, code == 1058
                ? "Защита системы выключена для диска C: — точку создать нельзя. Включите: Панель управления → Восстановление → Настройка восстановления системы."
                : $"Windows вернула код {code}. Скорее всего, отключена защита системы.");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException)
        {
            Log.Error("Не удалось создать точку восстановления", ex);
            return new RestorePointResult(false, ex.Message);
        }
    }
}
