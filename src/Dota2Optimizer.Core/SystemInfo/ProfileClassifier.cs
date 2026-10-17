using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.SystemInfo;

/// <summary>
/// Puts the PC into one of three buckets that drive the recommended presets.
/// It is a heuristic by design: names and VRAM, then CPU/RAM can only lower the result.
/// </summary>
public static partial class ProfileClassifier
{
    private const double OneGb = 1024d * 1024 * 1024;

    // Old/entry discrete cards that struggle with Dota 2 at 1080p high.
    [GeneratedRegex(@"\bGT\s?\d{3,4}\b|\bGTS\s?\d{3}\b|GTX\s?[4-79]\d\d\b|GTX\s?10(3|5)0|\bMX\s?\d{3}|Radeon\s?HD|\bR[579]\s?\d{3}|\bRX\s?(4\d0|5[56]0)\b|Arc.*A3[18]0", RegexOptions.IgnoreCase)]
    private static partial Regex WeakDiscrete();

    // Mid-range: still fine for "Balance" at 1080p.
    [GeneratedRegex(@"GTX\s?10[6-8]0|GTX\s?16\d0|RTX\s?(2050|3050)|\bRX\s?(5[789]0|5500|6400|6500|6600)\b|Arc.*A5[78]0", RegexOptions.IgnoreCase)]
    private static partial Regex MediumDiscrete();

    // Integrated GPUs that are decent with fast dual-channel memory.
    [GeneratedRegex(@"Radeon.*(7[68]0M|8[089]0M|8060S)|Arc.*1[34]0[VT]|Arc\(TM\)\s+Graphics", RegexOptions.IgnoreCase)]
    private static partial Regex StrongIntegrated();

    public static bool LooksIntegrated(string name, int vendorId, ulong dedicatedVideoMemory)
    {
        if (vendorId == 0x8086)
        {
            // Intel discrete Arc: A-series (A310…A770) and B-series (B570/B580).
            return !Regex.IsMatch(name, @"Arc.*\b[AB]\d{3}\b", RegexOptions.IgnoreCase);
        }

        if (vendorId is 0x1002 or 0x1022)
        {
            if (Regex.IsMatch(name, @"\bR[XV]\s?\d{3,4}|\bR[579]\s?\d{3}|Radeon\s?Pro|HD\s?\d{4}", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(name, @"\d{3}M\b|Vega\s?\d+\b", RegexOptions.IgnoreCase))
            {
                return false;
            }

            // "AMD Radeon(TM) Graphics", "Radeon 780M", "Vega 8"… and anything with tiny dedicated memory.
            return dedicatedVideoMemory < OneGb || Regex.IsMatch(name, @"Radeon\(TM\)\s+Graphics|\d{3}M\b|Vega\s?\d+\b", RegexOptions.IgnoreCase);
        }

        return false;
    }

    public static PcProfile Classify(GpuInfo? gpu, CpuInfo? cpu, MemoryInfo? memory)
    {
        var profile = ClassifyGpu(gpu, memory);

        var ramGb = memory?.TotalGb ?? 0;
        var threads = cpu?.LogicalProcessors ?? 0;

        if ((memory is not null && ramGb < 7.5) || (cpu is not null && threads < 4))
        {
            return PcProfile.Weak;
        }

        if (profile == PcProfile.Strong && ((memory is not null && ramGb < 15) || (cpu is not null && cpu.Cores < 6)))
        {
            return PcProfile.Medium;
        }

        return profile;
    }

    private static PcProfile ClassifyGpu(GpuInfo? gpu, MemoryInfo? memory)
    {
        if (gpu is null)
        {
            return PcProfile.Weak;
        }

        if (gpu.IsIntegrated)
        {
            var fastMemory = memory is not null && memory.TotalGb >= 15 && memory.LikelyDualChannel == true;
            return StrongIntegrated().IsMatch(gpu.Name) && fastMemory ? PcProfile.Medium : PcProfile.Weak;
        }

        if (WeakDiscrete().IsMatch(gpu.Name) || gpu.VramGb < 2.5)
        {
            return PcProfile.Weak;
        }

        if (MediumDiscrete().IsMatch(gpu.Name) || gpu.VramGb < 7.5)
        {
            return PcProfile.Medium;
        }

        return PcProfile.Strong;
    }

    public static string Describe(PcProfile p) => p switch
    {
        PcProfile.Weak => "Слабый (встроенная/старая видеокарта или мало памяти)",
        PcProfile.Medium => "Средний",
        _ => "Мощный",
    };
}
