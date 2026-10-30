using System.Text.RegularExpressions;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Upscale;

public enum CapStatus
{
    Works,
    Experimental,
    NotAvailable,
}

public enum CapKind
{
    /// <summary>Works over any game window (external tool).</summary>
    Overlay,

    /// <summary>Only in games that ship it.</summary>
    InGame,

    /// <summary>A switch in the video driver.</summary>
    Driver,
}

public sealed record Capability(string Id, string Name, CapKind Kind, CapStatus Status, string Why, bool FrameGeneration = false);

/// <summary>The family a video card belongs to, parsed from its name (what the features below depend on).</summary>
public sealed partial record GpuFamily(GpuVendor Vendor, string Name, bool Integrated)
{
    [GeneratedRegex(@"RTX\s?(\d{2})\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex Rtx();

    [GeneratedRegex(@"GTX\s?(\d{3,4})|GT\s?(\d{3,4})", RegexOptions.IgnoreCase)]
    private static partial Regex Gtx();

    [GeneratedRegex(@"RX\s?(\d)(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex Rx4();

    [GeneratedRegex(@"Radeon\s?(\d{3})M", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonIgpu();

    /// <summary>RTX generation: 20, 30, 40, 50; 0 = not RTX.</summary>
    public int RtxGen => Vendor == GpuVendor.Nvidia && Rtx().Match(Name) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>GTX/GT model number (470, 1060, 1650…); 0 = none.</summary>
    public int GtxModel
    {
        get
        {
            var m = Gtx().Match(Name);
            return Vendor == GpuVendor.Nvidia && m.Success ? int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : 0;
        }
    }

    /// <summary>NVIDIA Maxwell or newer (GTX 750/900 and up): the drivers that still get NIS and current features.</summary>
    public bool NvidiaMaxwellPlus => RtxGen > 0 || GtxModel >= 900 || GtxModel is 750 or 745 || Name.Contains("MX", StringComparison.OrdinalIgnoreCase);

    /// <summary>AMD RX series digit: 5 = RDNA1 (RX 5000), 6 = RDNA2, 7 = RDNA3, 9 = RDNA4; 0 = older or unknown.</summary>
    public int RdnaSeries
    {
        get
        {
            if (Vendor != GpuVendor.Amd)
            {
                return 0;
            }

            if (Rx4().Match(Name) is { Success: true } m && m.Groups[1].Value is "5" or "6" or "7" or "9")
            {
                return int.Parse(m.Groups[1].Value);
            }

            // Ryzen integrated: 680M/660M = RDNA2, 7x0M/8x0M/890M = RDNA3/3.5.
            if (RadeonIgpu().Match(Name) is { Success: true } i)
            {
                var n = int.Parse(i.Groups[1].Value);
                return n >= 740 ? 7 : n >= 610 ? 6 : 0;
            }

            return 0;
        }
    }

    public bool IntelArc => Vendor == GpuVendor.Intel && Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

    public static GpuFamily Of(GpuInfo g) => new(g.Vendor, g.Name, g.IsIntegrated);
}

/// <summary>
/// «Что доступно на вашей карте»: every upscaling / frame generation option, honestly marked for this card.
/// The rules follow the vendors' published requirements; where it depends on the game, the status says so.
/// </summary>
public static class CapabilityMatrix
{
    public static IReadOnlyList<Capability> For(GpuInfo gpu, GpuCaps caps, int windowsBuild)
    {
        var f = GpuFamily.Of(gpu);
        var list = new List<Capability>();
        var fl11 = caps.Fl11 || caps.FeatureLevel == 0; // unknown level: don't claim "no"
        var weak = f.Integrated || (f.Vendor == GpuVendor.Nvidia && f.RtxGen == 0 && f.GtxModel is > 0 and < 1060) || (f.Vendor == GpuVendor.Amd && f.RdnaSeries == 0);
        var wgc = windowsBuild == 0 || windowsBuild >= 18362;

        list.Add(fl11 && wgc
            ? new Capability("magpie", "Оконный апскейлер (Magpie): FSR 1, NIS, CAS, Lanczos, integer, Anime4K", CapKind.Overlay, weak ? CapStatus.Experimental : CapStatus.Works,
                weak ? "Работает, но на слабой или встроенной графике тяжёлые фильтры (Anime4K, FSR в 4K) сами съедят FPS — начинайте с FSR/Lanczos и масштаба ×1.5." : "Игра в окне или «окне без рамки» в меньшем разрешении, картинка растягивается на весь экран с повышением чёткости. Подходит для любой игры.")
            : new Capability("magpie", "Оконный апскейлер (Magpie)", CapKind.Overlay, CapStatus.NotAvailable,
                !fl11 ? "Нужен DirectX 11 feature level 11_0 — эта видеокарта его не поддерживает." : "Нужна Windows 10 1903 или новее (захват окна Windows.Graphics.Capture)."));

        list.Add(fl11
            ? new Capability("lossless", "Lossless Scaling (LSFG): масштабирование и генерация кадров ×2–×4", CapKind.Overlay, weak ? CapStatus.Experimental : CapStatus.Works,
                weak ? "Платная программа из Steam. На слабой карте генерация кадров сама требует ресурсов: режим Performance, ×2, лимит FPS на половину частоты монитора." : "Платная программа из Steam, работает поверх любой игры в окне. Генерация кадров добавляет задержку ввода.", FrameGeneration: true)
            : new Capability("lossless", "Lossless Scaling (LSFG)", CapKind.Overlay, CapStatus.NotAvailable, "Нужен DirectX 11 feature level 11_0.", FrameGeneration: true));

        switch (f.Vendor)
        {
            case GpuVendor.Nvidia:
                list.Add(f.RtxGen >= 20
                    ? new Capability("dlss", "DLSS Super Resolution (в играх)", CapKind.InGame, CapStatus.Works, "Включается в настройках игры, если она поддерживает DLSS.")
                    : new Capability("dlss", "DLSS Super Resolution", CapKind.InGame, CapStatus.NotAvailable, "Нужна видеокарта GeForce RTX (тензорные ядра)."));
                list.Add(f.RtxGen >= 40
                    ? new Capability("dlssfg", f.RtxGen >= 50 ? "DLSS Frame Generation и Multi Frame Generation (в играх)" : "DLSS Frame Generation (в играх)", CapKind.InGame, CapStatus.Works,
                        "В играх с поддержкой. Нужен включённый HAGS (Твики Windows → Видеокарта).", FrameGeneration: true)
                    : new Capability("dlssfg", "DLSS Frame Generation", CapKind.InGame, CapStatus.NotAvailable, "Только RTX 40 и RTX 50 — это аппаратное ограничение NVIDIA.", FrameGeneration: true));
                list.Add(f.NvidiaMaxwellPlus
                    ? new Capability("nis", "NVIDIA Image Scaling (в драйвере)", CapKind.Driver, CapStatus.Works, "Панель управления NVIDIA → Управление параметрами 3D → Масштабирование изображения. Работает в полноэкранных играх.")
                    : new Capability("nis", "NVIDIA Image Scaling (в драйвере)", CapKind.Driver, CapStatus.NotAvailable, "Для этой серии нет драйвера с NIS (нужна GTX 750/900 и новее). Используйте оконный апскейлер — там есть тот же NIS."));
                break;
            case GpuVendor.Amd:
                list.Add(f.RdnaSeries >= 6
                    ? new Capability("afmf", "AMD Fluid Motion Frames 2 (в драйвере)", CapKind.Driver, CapStatus.Works,
                        "Генерация кадров для любой DX11/DX12/Vulkan игры: AMD Software → Игры → Графика → AMD Fluid Motion Frames. Лучше всего при 60+ FPS без неё.", FrameGeneration: true)
                    : new Capability("afmf", "AMD Fluid Motion Frames", CapKind.Driver, CapStatus.NotAvailable, "Нужна Radeon RX 6000 и новее (или встроенная Radeon 680M/700M/800M).", FrameGeneration: true));
                list.Add(f.RdnaSeries >= 5
                    ? new Capability("rsr", "Radeon Super Resolution (в драйвере)", CapKind.Driver, CapStatus.Works, "AMD Software → Игры → Графика → Radeon Super Resolution. Игра в полноэкранном режиме с разрешением ниже экрана.")
                    : new Capability("rsr", "Radeon Super Resolution", CapKind.Driver, CapStatus.NotAvailable, "Нужна Radeon RX 5000 и новее."));
                break;
            case GpuVendor.Intel:
                list.Add(new Capability("xess", "Intel XeSS (в играх)", CapKind.InGame, f.IntelArc ? CapStatus.Works : CapStatus.Experimental,
                    f.IntelArc ? "На Arc работает в полном качестве (XMX-ядра)." : "На встроенной графике работает упрощённая версия (DP4a) — прирост меньше, чем на Arc."));
                break;
        }

        list.Add(caps.Dx12
            ? new Capability("fsr3", "AMD FSR 3 / FSR 4 и XeSS: апскейл в играх", CapKind.InGame, CapStatus.Works,
                "Работают на любой современной видеокарте в играх, где они есть (FSR 4 — только Radeon RX 9000).")
            : new Capability("fsr3", "AMD FSR 3 и XeSS в играх", CapKind.InGame, fl11 ? CapStatus.Experimental : CapStatus.NotAvailable,
                fl11 ? "FSR 2/3 апскейл есть и в DX11-играх, но многие новые игры требуют DirectX 12, которого у карты нет." : "Нужен DirectX 11 feature level 11_0 или DirectX 12."));

        var fsrFgOk = caps.Dx12 && (f.RtxGen >= 20 || f.RdnaSeries >= 5 || f.IntelArc);
        list.Add(new Capability("fsr3fg", "FSR 3 Frame Generation (в играх)", CapKind.InGame,
            fsrFgOk ? CapStatus.Works : caps.Dx12 ? CapStatus.Experimental : CapStatus.NotAvailable,
            fsrFgOk ? "В играх с поддержкой FSR 3 FG; работает и на картах NVIDIA."
                : caps.Dx12 ? "Запускается и на более старых DX12-картах, но AMD рекомендует RX 5700 / RTX 2070 и выше — на слабой карте прирост мал, а задержка заметна."
                : "Нужен DirectX 12.", FrameGeneration: true));

        list.Add(caps.Dx12
            ? new Capability("optiscaler", "OptiScaler: FSR/XeSS вместо DLSS и генерация кадров OptiFG", CapKind.InGame, CapStatus.Experimental,
                "Только одиночные игры с DLSS 2+/FSR 2+/XeSS. Сторонний мод, ставится в папку игры с бэкапом; в онлайн-играх с античитом — бан.", FrameGeneration: true)
            : new Capability("optiscaler", "OptiScaler", CapKind.InGame, CapStatus.NotAvailable, "Генерация кадров OptiFG работает только в DirectX 12.", FrameGeneration: true));

        return list;
    }
}
