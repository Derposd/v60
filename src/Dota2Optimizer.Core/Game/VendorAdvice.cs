using System.Text.RegularExpressions;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Game;

public sealed record GuideStep(string Setting, string Value, string Why, Effectiveness Effectiveness, bool Recommended);

public sealed record VendorGuide(string Title, string HowToOpen, IReadOnlyList<GuideStep> Steps, IReadOnlyList<string> Notes, IReadOnlyList<string> PanelCandidates);

/// <summary>
/// Step-by-step driver panel recommendations. We only give instructions: driver profiles are
/// never edited directly because there is no documented, reversible API for it.
/// </summary>
public static class VendorAdvice
{
    public static VendorGuide For(GpuInfo? gpu, SystemSnapshot s)
    {
        if (gpu is null)
        {
            return new VendorGuide("Видеокарта не определена", string.Empty, Array.Empty<GuideStep>(), new[] { "Установите драйвер видеокарты." }, Array.Empty<string>());
        }

        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => Nvidia(gpu, s),
            GpuVendor.Amd when gpu.IsIntegrated => AmdIntegrated(gpu, s),
            GpuVendor.Amd => Amd(gpu),
            _ => Intel(gpu, s),
        };
    }

    private static VendorGuide Nvidia(GpuInfo gpu, SystemSnapshot s)
    {
        var rtx = Regex.IsMatch(gpu.Name, @"RTX", RegexOptions.IgnoreCase);
        var maxwellPascal = Regex.IsMatch(gpu.Name, @"GTX\s?(9\d\d|10\d0)|\bGT\s?10\d0|Titan X", RegexOptions.IgnoreCase);
        var steps = new List<GuideStep>
        {
            new("Режим управления электропитанием", s.IsLaptop ? "Оптимальная производительность / Нормальный" : "Предпочтителен режим максимальной производительности",
                "Не даёт карте сбрасывать частоты в лёгких сценах — меньше скачков FPS. На ноутбуке повышает нагрев, поэтому там не обязательно.",
                Effectiveness.Low, Recommended: !s.IsLaptop),
            new("Режим низкой задержки", "Вкл (если в игре включён Reflex — оставьте «Выкл», Reflex важнее)",
                "Уменьшает очередь заранее подготовленных кадров, когда упираетесь в видеокарту. В Dota 2 есть встроенный NVIDIA Reflex (Настройки → Видео) — он работает лучше.",
                Effectiveness.Low, Recommended: true),
            new("Размер кэша шейдеров", "По умолчанию драйвера (или 10 ГБ, если места мало и бывают фризы)",
                "Кеш хранит скомпилированные шейдеры; если он слишком мал, после каждого запуска будут фризы перекомпиляции.",
                Effectiveness.Medium, Recommended: true),
            new("Вертикальный синхроимпульс", "G-Sync есть: «Вкл» в панели, в игре V-Sync выкл, ограничение FPS на 3 ниже частоты. G-Sync нет: «Выкл»",
                "Классическая схема для мониторов с переменной частотой: без разрывов и без лишней задержки. Без G-Sync V-Sync добавляет задержку.",
                Effectiveness.HardwareDependent, Recommended: true),
            new("Фильтрация текстур — качество", "Высокая производительность",
                "Немного упрощает фильтрацию текстур. Прирост обычно 1–2%, на слабых картах чуть больше.",
                Effectiveness.Low, Recommended: false),
        };
        var notes = new List<string>
        {
            "Панель управления NVIDIA → Управление параметрами 3D → Программные настройки → добавить dota2.exe (папка игры\\game\\bin\\win64). Меняйте настройки для программы, а не глобально.",
        };
        if (rtx)
        {
            notes.Add("В Dota 2 включите NVIDIA Reflex: Настройки → Видео. Это официальная поддержка NVIDIA (работает на GTX 900 и новее).");
        }

        if (maxwellPascal)
        {
            notes.Add("Серии GTX 900/10: по данным NVIDIA ветка драйверов 580 последняя с игровыми оптимизациями для Maxwell/Pascal; дальше выходят только исправления. Не ищите «более новый» драйвер, если 581.xx уже установлен.");
        }

        if (s.IsHybridGraphics)
        {
            notes.Add("Ноутбук с гибридной графикой: в той же панели в «Программных настройках» выберите для dota2.exe «Высокопроизводительный процессор NVIDIA», либо используйте твик предпочтения GPU в Windows.");
        }

        return new VendorGuide(gpu.Name, "ПКМ по рабочему столу → Панель управления NVIDIA (или кнопка ниже)", steps, notes, new[]
        {
            @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel",
            @"C:\Program Files\NVIDIA Corporation\Control Panel Client\nvcplui.exe",
        });
    }

    private static VendorGuide Amd(GpuInfo gpu)
    {
        var rdna = Regex.IsMatch(gpu.Name, @"RX\s?[5-9]\d{3}", RegexOptions.IgnoreCase);
        var rdna12 = Regex.IsMatch(gpu.Name, @"RX\s?[56]\d{3}", RegexOptions.IgnoreCase);
        var steps = new List<GuideStep>
        {
            new("Radeon Anti-Lag", rdna ? "Вкл (в Dota 2 работает Anti-Lag 2 в режиме DX11)" : "Вкл",
                "Снижает задержку ввода, когда упираетесь в видеокарту. AMD добавила Anti-Lag 2 для Dota 2 в драйвере 24.7.1 (карты RDNA, режим DX11).",
                Effectiveness.Low, Recommended: true),
            new("Radeon Chill", "Выкл (или вкл с мин/макс = частоте монитора на ноутбуке)",
                "Снижает FPS при отсутствии движения для экономии энергии. Для соревновательной игры лучше выкл; на ноутбуке может снизить нагрев.",
                Effectiveness.HardwareDependent, Recommended: false),
            new("Enhanced Sync", "Выкл",
                "Иногда вызывает рывки и чёрные экраны. Если есть FreeSync — используйте его, а не Enhanced Sync.",
                Effectiveness.Low, Recommended: true),
            new("Radeon Image Sharpening", "По желанию",
                "Повышает резкость, особенно если снижен рендер-масштаб. Почти не влияет на FPS.",
                Effectiveness.Low, Recommended: false),
            new("Кеш шейдеров", "Оптимизировано AMD (сброс — только при фризах после обновления драйвера)",
                "Сброс кеша лечит фризы после смены драйвера, но первые матчи после него будут подтормаживать.",
                Effectiveness.Medium, Recommended: true),
            new("FreeSync", "Вкл, если монитор поддерживает; FPS ограничить на 3 ниже частоты",
                "Без разрывов и без задержки V-Sync.",
                Effectiveness.HardwareDependent, Recommended: true),
        };
        var notes = new List<string>
        {
            "AMD Software: Adrenalin → Игры → Dota 2 (настройки для игры, а не глобальный профиль). Профиль «HYPR-RX» включает сразу несколько функций — проверяйте замером.",
            "Если бывает «чёрный экран» или сообщение о тайм-ауте драйвера — смотрите вкладку «Диагностика вылетов»: там ищутся события amdkmdag/Display 4101 (TDR).",
        };
        if (rdna12)
        {
            notes.Add("RX 5000/6000: с драйвера 25.10.2 AMD перевела эти карты в отдельную ветку обслуживания; игровые исправления выходят по мере необходимости и реже.");
        }

        return new VendorGuide(gpu.Name, "ПКМ по рабочему столу → AMD Software: Adrenalin Edition (или кнопка ниже)", steps, notes, new[]
        {
            @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
        });
    }

    private static VendorGuide AmdIntegrated(GpuInfo gpu, SystemSnapshot s)
    {
        var guide = Amd(gpu);
        var notes = guide.Notes.ToList();
        notes.Insert(0, "Встроенная графика Radeon: главное — двухканальная память с высокой частотой (включите XMP/EXPO в BIOS) и выделенный объём видеопамяти в BIOS (UMA Frame Buffer) 2–4 ГБ, если есть такая настройка.");
        if (s.Memory?.LikelyDualChannel == false)
        {
            notes.Insert(0, "Похоже, установлен один модуль памяти — для встроенной графики это большая потеря FPS.");
        }

        return guide with { Notes = notes };
    }

    private static VendorGuide Intel(GpuInfo gpu, SystemSnapshot s)
    {
        var arc = gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        var steps = new List<GuideStep>
        {
            new("Пресет Dota 2", "«Максимум FPS», рендер-масштаб 70–85%",
                "Встроенная графика ограничена пропускной способностью памяти — снижение масштаба рендера даёт больше всего.",
                Effectiveness.High, Recommended: !arc),
            new("Ограничение FPS", "Под частоту монитора (обычно 60)",
                "Встроенная графика делит лимит мощности с процессором: меньше лишних кадров — выше частоты CPU и ровнее кадры.",
                Effectiveness.Medium, Recommended: true),
            new("Режим питания", "Максимальная производительность, ноутбук от сети",
                "На батарее частота iGPU сильно режется.",
                Effectiveness.High, Recommended: true),
            new("Память", "Два модуля (двухканальный режим), XMP в BIOS",
                "Для iGPU это самый важный «апгрейд»: в одноканальном режиме FPS может быть заметно ниже.",
                Effectiveness.High, Recommended: true),
        };
        var notes = new List<string>
        {
            arc ? "Intel Graphics Software / Arc Control: проверьте наличие свежего драйвера; для Arc сравните DX11 и Vulkan."
                : "Intel Graphics Command Center / Intel Graphics Software: включите режим «Максимальная производительность» для Dota 2, если доступен.",
        };
        if (s.Memory?.LikelyDualChannel == false)
        {
            notes.Insert(0, "Похоже, установлен один модуль памяти — для встроенной графики это большая потеря FPS.");
        }

        return new VendorGuide(gpu.Name, "Пуск → Intel Graphics Software (или Intel Graphics Command Center)", steps, notes, new[]
        {
            @"shell:AppsFolder\AppUp.IntelGraphicsExperience_8j3eq9eme6ctt!App",
        });
    }
}
