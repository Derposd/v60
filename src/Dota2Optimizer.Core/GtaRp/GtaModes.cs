using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.GtaRp;

public enum GtaModeKind
{
    /// <summary>Picked by how strong the PC is: more FPS, simpler picture.</summary>
    Performance,

    /// <summary>For roleplay itself: how the game looks.</summary>
    Roleplay,
}

/// <summary>One ready-made set of GTA V graphics values.</summary>
public sealed record GtaMode(
    string Id,
    string Title,
    string Tagline,
    GtaModeKind Kind,
    Effectiveness Effect,
    string Explanation,
    IReadOnlyDictionary<string, string> Values,
    bool LowerResolution = false)
{
    /// <summary>Texture quality is also limited by video memory (see <see cref="GtaModes.Resolve"/>).</summary>
    public bool TexturesFollowVram => Id != GtaModes.Potato;
}

/// <summary>
/// The five modes. Key names and the 0…3 quality scale come from GTA V's own settings.xml (file version 27); every value stays inside
/// what the in-game menu offers, so a mode never produces a picture the game itself could not.
/// </summary>
public static class GtaModes
{
    public const string Potato = "potato";
    public const string Weak = "weak";
    public const string Medium = "medium";
    public const string Pretty = "pretty";
    public const string Hybrid = "hybrid";

    public const int LowResWidth = 1280;
    public const int LowResHeight = 720;

    private const ulong Gb = 1024UL * 1024 * 1024;

    // Keys that only appear in the "advanced graphics" block and are the same "off" everywhere they are not wanted.
    private static Dictionary<string, string> D(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    public static IReadOnlyList<GtaMode> All { get; } = new[]
    {
        new GtaMode(Potato, "Ультра-слабый ПК", "Мыльная картинка, максимум FPS", GtaModeKind.Performance, Effectiveness.High,
            "Для ноутбуков со встроенной графикой, видеокарт до 2 ГБ и 8 ГБ памяти. Всё выключено или на минимуме: тени, отражения, трава, частицы, сглаживание, " +
            "дальность прорисовки сокращена. Текстуры «нормальные», разрешение на 16:9-мониторах снижается до 1280×720 — картинка заметно «мылится», зато игра остаётся играбельной.",
            D(("Tessellation", "0"), ("LodScale", "0.4"), ("PedLodBias", "0"), ("VehicleLodBias", "0"), ("ShadowQuality", "0"),
              ("ReflectionQuality", "0"), ("ReflectionMSAA", "0"), ("SSAO", "0"), ("AnisotropicFiltering", "0"), ("MSAA", "0"),
              ("MSAAFragments", "0"), ("MSAAQuality", "0"), ("SamplingMode", "0"), ("TextureQuality", "0"), ("ParticleQuality", "0"),
              ("WaterQuality", "0"), ("GrassQuality", "0"), ("ShaderQuality", "0"), ("Shadow_SoftShadows", "0"),
              ("UltraShadows_Enabled", "false"), ("Shadow_ParticleShadows", "false"), ("Shadow_Distance", "0.6"), ("Shadow_LongShadows", "false"),
              ("Reflection_MipBlur", "false"), ("FXAA_Enabled", "false"), ("TXAA_Enabled", "false"), ("Lighting_FogVolumes", "false"),
              ("PostFX", "0"), ("DoF", "false"), ("HdStreamingInFlight", "false"), ("MaxLodScale", "0"), ("MotionBlurStrength", "0"), ("VSync", "0")),
            LowerResolution: true),

        new GtaMode(Weak, "Слабый ПК", "Играбельно и без лишнего", GtaModeKind.Performance, Effectiveness.High,
            "Для видеокарт уровня GTX 750/1050 и офисных ПК. Тени, отражения и тяжёлые эффекты выключены, дальность прорисовки умеренная, " +
            "включено лёгкое сглаживание FXAA. Разрешение не меняется.",
            D(("Tessellation", "0"), ("LodScale", "0.7"), ("PedLodBias", "0.1"), ("VehicleLodBias", "0.1"), ("ShadowQuality", "0"),
              ("ReflectionQuality", "0"), ("ReflectionMSAA", "0"), ("SSAO", "0"), ("AnisotropicFiltering", "0"), ("MSAA", "0"),
              ("MSAAFragments", "0"), ("MSAAQuality", "0"), ("SamplingMode", "0"), ("TextureQuality", "1"), ("ParticleQuality", "0"),
              ("WaterQuality", "0"), ("GrassQuality", "0"), ("ShaderQuality", "0"), ("Shadow_SoftShadows", "0"),
              ("UltraShadows_Enabled", "false"), ("Shadow_ParticleShadows", "false"), ("Shadow_Distance", "0.8"), ("Shadow_LongShadows", "false"),
              ("Reflection_MipBlur", "false"), ("FXAA_Enabled", "true"), ("TXAA_Enabled", "false"), ("Lighting_FogVolumes", "false"),
              ("PostFX", "0"), ("DoF", "false"), ("HdStreamingInFlight", "false"), ("MaxLodScale", "0"), ("MotionBlurStrength", "0"), ("VSync", "0"))),

        new GtaMode(Medium, "Средний ПК", "Ровный FPS, приличная картинка", GtaModeKind.Performance, Effectiveness.Medium,
            "Для GTX 1060 / RX 580 и похожих. Тени и отражения на «нормальном», сглаживание FXAA, текстуры по объёму видеопамяти, " +
            "тяжёлые эффекты (тесселяция, мягкие тени, объёмный туман, размытие) выключены.",
            D(("Tessellation", "0"), ("LodScale", "0.85"), ("ShadowQuality", "1"), ("ReflectionQuality", "1"), ("ReflectionMSAA", "0"),
              ("SSAO", "1"), ("AnisotropicFiltering", "4"), ("MSAA", "0"), ("MSAAFragments", "0"), ("MSAAQuality", "0"), ("SamplingMode", "0"),
              ("TextureQuality", "2"), ("ParticleQuality", "1"), ("WaterQuality", "1"), ("GrassQuality", "1"), ("ShaderQuality", "1"),
              ("Shadow_SoftShadows", "1"), ("UltraShadows_Enabled", "false"), ("Shadow_ParticleShadows", "false"), ("Shadow_Distance", "1"),
              ("Shadow_LongShadows", "false"), ("Reflection_MipBlur", "false"), ("FXAA_Enabled", "true"), ("TXAA_Enabled", "false"),
              ("Lighting_FogVolumes", "false"), ("PostFX", "1"), ("DoF", "false"), ("HdStreamingInFlight", "false"), ("MaxLodScale", "0"),
              ("MotionBlurStrength", "0"))),

        new GtaMode(Pretty, "РП красиво", "Почти максимум, без лишнего", GtaModeKind.Roleplay, Effectiveness.HardwareDependent,
            "Для мощных ПК (от GTX 1660 / RX 6600 и 16 ГБ памяти). Высокие текстуры, тени, отражения и SSAO, анизотропная фильтрация 16×, FXAA, трава, вода и туман. " +
            "Отключено только то, что в РП ничего не добавляет, но стоит FPS: размытие движения, глубина резкости, MSAA, сверхмягкие и удлинённые тени.",
            D(("Tessellation", "1"), ("LodScale", "1"), ("ShadowQuality", "2"), ("ReflectionQuality", "2"), ("ReflectionMSAA", "0"),
              ("SSAO", "2"), ("AnisotropicFiltering", "16"), ("MSAA", "0"), ("MSAAFragments", "0"), ("MSAAQuality", "0"), ("SamplingMode", "0"),
              ("TextureQuality", "2"), ("ParticleQuality", "2"), ("WaterQuality", "2"), ("GrassQuality", "2"), ("ShaderQuality", "2"),
              ("Shadow_SoftShadows", "2"), ("UltraShadows_Enabled", "false"), ("Shadow_ParticleShadows", "true"), ("Shadow_Distance", "1"),
              ("Shadow_LongShadows", "false"), ("Reflection_MipBlur", "true"), ("FXAA_Enabled", "true"), ("TXAA_Enabled", "false"),
              ("Lighting_FogVolumes", "true"), ("PostFX", "2"), ("DoF", "false"), ("HdStreamingInFlight", "false"), ("MaxLodScale", "0"),
              ("MotionBlurStrength", "0"))),

        new GtaMode(Hybrid, "Гибрид", "Красиво и без просадок", GtaModeKind.Roleplay, Effectiveness.Medium,
            "Совмещает оба подхода. Берёт из «РП красиво» то, что видно сразу: чёткие текстуры, фильтрацию 8×, сглаживание, отражения. " +
            "И берёт из режима для среднего ПК то, что дорого, но малозаметно: тени, SSAO, тесселяцию и дальность прорисовки. " +
            "Хороший выбор, если нужна приятная картинка, но FPS в толпе на спавне важнее.",
            D(("Tessellation", "0"), ("LodScale", "0.85"), ("ShadowQuality", "1"), ("ReflectionQuality", "1"), ("ReflectionMSAA", "0"),
              ("SSAO", "1"), ("AnisotropicFiltering", "8"), ("MSAA", "0"), ("MSAAFragments", "0"), ("MSAAQuality", "0"), ("SamplingMode", "0"),
              ("TextureQuality", "2"), ("ParticleQuality", "1"), ("WaterQuality", "1"), ("GrassQuality", "1"), ("ShaderQuality", "2"),
              ("Shadow_SoftShadows", "1"), ("UltraShadows_Enabled", "false"), ("Shadow_ParticleShadows", "false"), ("Shadow_Distance", "0.9"),
              ("Shadow_LongShadows", "false"), ("Reflection_MipBlur", "true"), ("FXAA_Enabled", "true"), ("TXAA_Enabled", "false"),
              ("Lighting_FogVolumes", "false"), ("PostFX", "1"), ("DoF", "false"), ("HdStreamingInFlight", "false"), ("MaxLodScale", "0"),
              ("MotionBlurStrength", "0"))),
    };

    public static GtaMode Get(string id) => All.First(m => m.Id == id);

    /// <summary>
    /// The mode's values for this video card. Textures are the one setting that must follow video memory: too much of it makes the
    /// game stutter every few seconds while it swaps textures through system memory.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolve(GtaMode mode, GpuInfo? gpu)
    {
        var values = new Dictionary<string, string>(mode.Values);
        if (mode.TexturesFollowVram && values.TryGetValue("TextureQuality", out var wanted) && int.TryParse(wanted, out var level))
        {
            values["TextureQuality"] = Math.Min(level, TextureCap(gpu)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>0 = normal, 1 = high, 2 = very high. Unknown memory means no limit.</summary>
    public static int TextureCap(GpuInfo? gpu)
    {
        if (gpu is null)
        {
            return 2;
        }

        if (gpu.IsIntegrated)
        {
            return 1;
        }

        if (gpu.DedicatedVideoMemory == 0)
        {
            return 2;
        }

        return gpu.DedicatedVideoMemory < 2 * Gb ? 0 : gpu.DedicatedVideoMemory < 3UL * Gb + Gb / 2 ? 1 : 2;
    }

    /// <summary>The mode whose values the file holds right now, or null when it matches none (the player's own settings).</summary>
    public static GtaMode? Detect(GtaSettingsFile file, GpuInfo? gpu)
    {
        foreach (var mode in All)
        {
            var values = Resolve(mode, gpu);
            var present = values.Where(kv => file.Has(kv.Key)).ToList();
            // A file that lacks most of the keys is not a reliable witness.
            if (present.Count >= values.Count * 0.8 && present.All(kv => file.Is(kv.Key, kv.Value)))
            {
                return mode;
            }
        }

        return null;
    }

    /// <summary>The mode that fits this PC best, with the reason in plain words.</summary>
    public static (GtaMode Mode, string Reason) Recommend(SystemSnapshot s)
    {
        var gpu = s.PrimaryGpu;
        var ramGb = s.Memory?.TotalGb ?? 0;
        var vramGb = gpu is null ? 0 : gpu.DedicatedVideoMemory / (double)Gb;

        var ultraWeak = gpu is null
                        || gpu.IsIntegrated
                        || (gpu.DedicatedVideoMemory > 0 && vramGb < 2.5)
                        || (s.Memory is not null && ramGb < 7.5);

        switch (s.Profile)
        {
            case PcProfile.Weak when ultraWeak:
                return (Get(Potato), gpu is null || gpu.IsIntegrated
                    ? "Встроенная графика или видеокарта не найдена: GTA V потянет только на минимальных настройках."
                    : s.Memory is not null && ramGb < 7.5
                        ? $"Мало оперативной памяти ({ramGb:0.#} ГБ): нужен самый лёгкий режим."
                        : $"У видеокарты мало памяти ({vramGb:0.#} ГБ): нужен самый лёгкий режим.");
            case PcProfile.Weak:
                return (Get(Weak), "ПК слабее среднего: лучше отключить тени, отражения и тяжёлые эффекты.");
            case PcProfile.Medium:
                return (Get(Hybrid), "ПК среднего уровня: «Гибрид» даёт чёткую картинку и не просаживает FPS в толпе. Нужно больше FPS — берите режим «Средний ПК».");
            default:
                return (Get(Pretty), "Мощный ПК: можно играть почти на максимальных настройках, отключив только лишнее.");
        }
    }
}
