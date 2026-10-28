using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Gpu;

/// <summary>
/// One DWORD of the NVIDIA global profile. Before anything is offered the id is checked against the driver: the driver must
/// know it and call it by the expected name, so a changed or unknown id is never written.
/// </summary>
public sealed class NvidiaSettingTweak : Tweak
{
    /// <summary>Driver settings source; replaced in tests.</summary>
    public static Func<INvidiaDrs> Drs { get; set; } = () => new NvidiaDrs();

    public required string TweakId { get; init; }
    public required string Name { get; init; }
    public required uint SettingId { get; init; }
    public required uint Value { get; init; }

    /// <summary>Part of the driver's own name for the setting (English), e.g. "Power management".</summary>
    public required string ExpectedName { get; init; }

    public required string Description { get; init; }
    public required Effectiveness Effect { get; init; }
    public string? Games { get; init; }
    public string? Manual { get; init; }
    public bool IsRecommended { get; init; }

    public override string Id => TweakId;
    public override string Title => Name;
    public override TweakCategory Category => TweakCategory.Gpu;
    public override Effectiveness Effectiveness => Effect;
    public override string Explanation => Description;
    public override bool Recommended => IsRecommended;
    public override string? AppliesTo => Games;
    public override string? ManualSteps => Manual;
    public override string Risk => "Низкий: меняется глобальный профиль драйвера, прежнее значение сохраняется и возвращается откатом. Обновления драйвера не затрагиваются. Нужны права администратора";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (!ctx.Snapshot.RealGpus.Any(g => g.Vendor == GpuVendor.Nvidia))
        {
            return Result(TweakStatus.NotApplicable, "Нет видеокарты NVIDIA.", recommendedHere: false);
        }

        var drs = Drs();
        if (!drs.Available)
        {
            return Result(TweakStatus.ManualOnly, "Драйвер NVIDIA не ответил (NVAPI). Настройте вручную в панели NVIDIA.", recommendedHere: false);
        }

        var name = drs.NameOf(SettingId);
        if (name is not null && !name.Contains(ExpectedName, StringComparison.OrdinalIgnoreCase))
        {
            return Result(TweakStatus.ManualOnly, $"Этот драйвер называет настройку иначе («{name}») — программа её не трогает. Настройте вручную в панели NVIDIA.", recommendedHere: false);
        }

        var current = drs.Get(SettingId);
        if (current == Value)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже так в глобальном профиле драйвера.");
        }

        return Result(TweakStatus.CanApply, current is null ? "Сейчас: по умолчанию драйвера." : $"Сейчас: {current}.", new[]
        {
            PlannedAction.SetNvidia(SettingId, Value, $"NVIDIA: {Name}"),
        });
    }
}

/// <summary>A setting that only the vendor's own panel can change (AMD Adrenalin, Intel Graphics Software): exact steps instead of a switch.</summary>
public sealed class VendorManualTweak : Tweak
{
    public required string TweakId { get; init; }
    public required string Name { get; init; }
    public required GpuVendor Vendor { get; init; }
    public required string Description { get; init; }
    public required string Steps { get; init; }
    public required Effectiveness Effect { get; init; }
    public string? Games { get; init; }

    public override string Id => TweakId;
    public override string Title => Name;
    public override TweakCategory Category => TweakCategory.Gpu;
    public override Effectiveness Effectiveness => Effect;
    public override string Explanation => Description;
    public override string? AppliesTo => Games;
    public override string? ManualSteps => Steps;
    public override string Risk => "Нет: настройка в панели драйвера, программа её не меняет";

    public override TweakPlan Plan(TweakContext ctx) =>
        ctx.Snapshot.RealGpus.Any(g => g.Vendor == Vendor)
            ? Result(TweakStatus.ManualOnly, "Настраивается в панели драйвера — шаги ниже.")
            : Result(TweakStatus.NotApplicable, "Другая видеокарта.", recommendedHere: false);
}

public static class GpuTweakCatalog
{
    public const uint PreferredPState = 0x1057EB71;
    public const uint PrerenderLimit = 0x007BA09E;
    public const uint ShaderCacheSize = 0x00AC8497;

    public static IReadOnlyList<Tweak> All { get; } = new Tweak[]
    {
        new NvidiaSettingTweak
        {
            TweakId = "gpu.nv.power",
            Name = "NVIDIA: режим управления электропитанием — «Максимальная производительность»",
            SettingId = PreferredPState,
            Value = 1,
            ExpectedName = "Power management",
            Effect = Effectiveness.HardwareDependent,
            Games = "Лёгкие и киберспортивные игры (CS2, Dota 2, Valorant), где карта загружена не полностью и драйвер сбрасывает частоты",
            Description = "Карта не снижает частоты между кадрами: меньше редких просадок, когда нагрузка скачет. Минус: в глобальном профиле частоты могут держаться " +
                          "и на рабочем столе — выше потребление и нагрев в простое. На ноутбуке от батареи не включайте.",
        },
        new NvidiaSettingTweak
        {
            TweakId = "gpu.nv.lowlatency",
            Name = "NVIDIA: режим низкой задержки — «Вкл»",
            SettingId = PrerenderLimit,
            Value = 1,
            ExpectedName = "pre-rendered",
            Effect = Effectiveness.Medium,
            Games = "DirectX 9–11, когда видеокарта загружена на 95–100%. В играх с NVIDIA Reflex (CS2, Valorant, Fortnite, Apex) включайте Reflex в игре — он лучше и заменяет этот режим. DX12/Vulkan его игнорируют",
            Description = "Драйвер держит в очереди не больше одного готового кадра: между нажатием и картинкой проходит меньше времени. FPS не добавляет.",
            Manual = "Режим «Ультра» — вручную: Панель управления NVIDIA → Управление параметрами 3D → Режим низкой задержки → Ультра.",
        },
        new NvidiaSettingTweak
        {
            TweakId = "gpu.nv.shadercache",
            Name = "NVIDIA: размер кэша шейдеров — 10 ГБ",
            SettingId = ShaderCacheSize,
            Value = 10240,
            ExpectedName = "Shader Cache",
            Effect = Effectiveness.Low,
            Games = "Большие игры на DX12/Vulkan, у которых статтер возвращается после обновлений: кэш по умолчанию переполняется и шейдеры собираются заново",
            Description = "Драйвер хранит до 10 ГБ скомпилированных шейдеров на диске. Займёт место на системном диске; на FPS не влияет — только на повторные подтормаживания.",
        },
        new VendorManualTweak
        {
            TweakId = "gpu.amd.antilag",
            Name = "AMD: Radeon Anti-Lag",
            Vendor = GpuVendor.Amd,
            Effect = Effectiveness.Medium,
            Games = "Когда видеокарта загружена полностью. В играх с Anti-Lag 2 (CS2, Ghost of Tsushima и др.) включайте его в самой игре",
            Description = "Аналог режима низкой задержки NVIDIA: процессор не убегает вперёд видеокарты, задержка ввода ниже. FPS не добавляет.",
            Steps = "AMD Software: Adrenalin Edition → Игры → Графика (глобально или для конкретной игры) → Radeon Anti-Lag: вкл.",
        },
        new VendorManualTweak
        {
            TweakId = "gpu.amd.chill",
            Name = "AMD: выключить Radeon Chill и Radeon Boost для соревновательных игр",
            Vendor = GpuVendor.Amd,
            Effect = Effectiveness.Medium,
            Games = "Шутеры и MOBA: Chill снижает FPS, когда мышь не двигается, Boost снижает разрешение при движении",
            Description = "Обе функции экономят энергию или поднимают FPS ценой стабильности картинки. В соревновательных играх они дают «плавающий» FPS и задержку.",
            Steps = "AMD Software → Игры → Графика → Radeon Chill: выкл., Radeon Boost: выкл. (можно только для нужной игры).",
        },
        new VendorManualTweak
        {
            TweakId = "gpu.intel.latency",
            Name = "Intel: режим низкой задержки и максимальная производительность",
            Vendor = GpuVendor.Intel,
            Effect = Effectiveness.HardwareDependent,
            Games = "Intel Arc и встроенная графика Iris Xe / Arc в ноутбуках",
            Description = "На встроенной графике самое важное — режим питания: от батареи и в режиме «энергосбережение» частоты GPU сильно режутся. На Arc есть режим низкой задержки.",
            Steps = "Intel Graphics Software (или Arc Control) → Графика → Low Latency: вкл. (Arc). Для встроенной: Параметры → Система → Питание → Режим питания: «Максимальная производительность», играть от сети.",
        },
    };
}
