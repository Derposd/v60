using System.Globalization;

namespace Dota2Optimizer.Core.SystemInfo;

public enum Severity
{
    Info,
    Warning,
    Critical,
}

public sealed record Advice(Severity Severity, string Title, string Details, string? Link = null);

/// <summary>
/// Offline driver checks. We cannot know the newest driver without internet, so we only judge age
/// and well-documented support changes, and point to the official download page.
/// </summary>
public static class DriverAdvisor
{
    public const string NvidiaDownload = "https://www.nvidia.com/Download/index.aspx";
    public const string AmdDownload = "https://www.amd.com/en/support/download/drivers.html";
    public const string IntelDownload = "https://www.intel.com/content/www/us/en/support/detect.html";

    /// <summary>
    /// NVIDIA's Windows driver version "32.0.15.6094" maps to the marketing version "560.94":
    /// the last five digits of the last two fields.
    /// </summary>
    public static string? NvidiaMarketingVersion(string windowsVersion)
    {
        var parts = windowsVersion.Split('.');
        if (parts.Length < 4)
        {
            return null;
        }

        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5 || !digits.All(char.IsDigit))
        {
            return null;
        }

        var last5 = digits[^5..];
        return $"{last5[..3]}.{last5[3..]}";
    }

    /// <summary>Intel "31.0.101.5590" → "101.5590".</summary>
    public static string? IntelMarketingVersion(string windowsVersion)
    {
        var parts = windowsVersion.Split('.');
        return parts.Length >= 4 ? $"{parts[2]}.{parts[3]}" : null;
    }

    public static string DownloadLink(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => NvidiaDownload,
        GpuVendor.Amd => AmdDownload,
        _ => IntelDownload,
    };

    public static IReadOnlyList<Advice> Check(GpuInfo gpu, WindowsInfo? windows, DateTime today)
    {
        var list = new List<Advice>();
        var link = DownloadLink(gpu.Vendor);

        if (gpu.DriverDate is { } date)
        {
            var ageDays = (today - date).TotalDays;
            var dateText = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            if (ageDays > 365)
            {
                list.Add(new Advice(Severity.Critical, "Драйвер видеокарты старше года",
                    $"Дата драйвера {gpu.Name}: {dateText}. Новые драйверы исправляют вылеты и фризы в Source 2. Скачайте драйвер с официального сайта (приложение ничего не скачивает само).", link));
            }
            else if (ageDays > 180)
            {
                list.Add(new Advice(Severity.Warning, "Драйвер видеокарты старше полугода",
                    $"Дата драйвера {gpu.Name}: {dateText}. Если есть вылеты или артефакты — обновите драйвер.", link));
            }
        }
        else
        {
            list.Add(new Advice(Severity.Info, "Не удалось определить дату драйвера", "Проверьте версию в панели управления видеокарты.", link));
        }

        if (gpu.Vendor == GpuVendor.Microsoft || gpu.DriverVersion.Length == 0)
        {
            list.Add(new Advice(Severity.Critical, "Не установлен драйвер видеокарты",
                "Используется «Базовый видеоадаптер Майкрософт». Игры будут работать очень медленно или не запустятся.", link));
        }

        if (windows is { IsWindows11: false })
        {
            list.Add(new Advice(Severity.Warning, "Windows 10",
                "Поддержка Windows 10 Microsoft завершилась 14.10.2025. NVIDIA объявила, что последние Game Ready драйверы для Windows 10 выходят в октябре 2026, дальше — только ежеквартальные исправления безопасности. Для новых драйверов понадобится Windows 11.",
                gpu.Vendor == GpuVendor.Nvidia ? NvidiaDownload : null));
        }

        return list;
    }
}
