using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Game;

public enum GameId
{
    Dota2,
    Cs2,
}

/// <summary>
/// Everything that differs between the supported Source 2 games: Steam app id, folders, process,
/// config file names, launch options, graphics presets and known issues. The engine (scan, plan, apply,
/// rollback, monitor) is shared; only this description changes.
/// </summary>
public sealed class GameDefinition
{
    public required GameId Id { get; init; }

    /// <summary>Full name for sentences: "Dota 2", "Counter-Strike 2".</summary>
    public required string Name { get; init; }

    /// <summary>Short name for buttons and the sidebar: "Dota 2", "CS2".</summary>
    public required string ShortName { get; init; }

    public required string AppId { get; init; }

    /// <summary>Folder under steamapps\common when the manifest does not say.</summary>
    public required string DefaultInstallDir { get; init; }

    /// <summary>Process name without ".exe".</summary>
    public required string ProcessName { get; init; }

    /// <summary>Mod folder under game\: "dota", "csgo".</summary>
    public required string ModDir { get; init; }

    public required string VideoFileName { get; init; }

    /// <summary>Prefix for game-specific tweak ids. Dota keeps the ids of earlier versions so old backups still match.</summary>
    public required string TweakPrefix { get; init; }

    /// <summary>Prefix for ids of one-click fixes (see <see cref="QuickFixes"/>).</summary>
    public required string FixPrefix { get; init; }

    /// <summary>Suffix for Windows tweaks bound to the game's exe (GPU preference, priority…). Empty for Dota for the same reason.</summary>
    public required string ExeTweakSuffix { get; init; }

    public required LaunchCatalog Launch { get; init; }
    public required IReadOnlyList<KnownIssue> Issues { get; init; }

    /// <summary>video settings for a preset: key → value in the game's video config.</summary>
    public required Func<VideoPreset, PcProfile, double, IReadOnlyDictionary<string, string>> BuildPreset { get; init; }

    /// <summary>What the graphics preset touches, for the tweak explanation.</summary>
    public required string PresetExplanation { get; init; }

    /// <summary>Exclusive fullscreen also needs setting.nowindowborder = 0 (Dota). CS2 only uses setting.fullscreen.</summary>
    public bool FullscreenUsesNoBorder { get; init; }

    /// <summary>Approximate free space a big update needs, GB.</summary>
    public int UpdateSpaceGb { get; init; } = 20;

    /// <summary>Minimum RAM the game is comfortable with, GB.</summary>
    public int MinRamGb { get; init; } = 8;

    /// <summary>Where the in-game FPS counter / Reflex / API switch live, used in advice texts.</summary>
    public required string ReflexHint { get; init; }

    public required string AmdAntiLagHint { get; init; }

    /// <summary>First line of the low-end (integrated graphics) advice.</summary>
    public required string LowEndPresetHint { get; init; }

    /// <summary>How to compare DX11 and Vulkan in this game.</summary>
    public required string HowToCompareApi { get; init; }

    public string ExeFileName => ProcessName + ".exe";

    /// <summary>Path to the game exe relative to the install folder.</summary>
    public string ExeRelativePath => @"game\bin\win64\" + ExeFileName;

    public string LaunchOptionsManualPath => $"Steam → Библиотека → {Name} → ПКМ → Свойства → Общие → Параметры запуска.";

    public string TweakId(string name) => $"{TweakPrefix}.{name}";

    public string ExeTweakId(string baseId) => baseId + ExeTweakSuffix;

    public override string ToString() => Name;
}

public static class Games
{
    public static GameDefinition Dota2 { get; } = new()
    {
        Id = GameId.Dota2,
        Name = "Dota 2",
        ShortName = "Dota 2",
        AppId = "570",
        DefaultInstallDir = "dota 2 beta",
        ProcessName = "dota2",
        ModDir = "dota",
        VideoFileName = "video.txt",
        TweakPrefix = "dota",
        FixPrefix = "fix.",
        ExeTweakSuffix = string.Empty,
        Launch = LaunchOptionCatalog.Dota,
        Issues = KnownIssues.Dota,
        BuildPreset = VideoPresets.BuildDota,
        PresetExplanation =
            "Меняет только уже существующие настройки графики в video.txt: тени, SSAO, туман, отражения, вода, рендер-масштаб, V-Sync. " +
            "Самый заметный способ поднять FPS, особенно на слабых ПК: тени и масштаб рендера — главные потребители видеокарты.",
        FullscreenUsesNoBorder = true,
        UpdateSpaceGb = 20,
        MinRamGb = 8,
        ReflexHint = "В Dota 2 включите NVIDIA Reflex: Настройки → Видео. Это официальная поддержка NVIDIA (работает на GTX 900 и новее).",
        AmdAntiLagHint = "AMD добавила Anti-Lag 2 для Dota 2 в драйвере 24.7.1 (карты RDNA, режим DX11).",
        LowEndPresetHint = "«Максимум FPS», рендер-масштаб 70–85%",
        HowToCompareApi = ApiAdvisor.HowToCompareDota,
    };

    public static GameDefinition Cs2 { get; } = new()
    {
        Id = GameId.Cs2,
        Name = "Counter-Strike 2",
        ShortName = "CS2",
        AppId = "730",
        DefaultInstallDir = "Counter-Strike Global Offensive",
        ProcessName = "cs2",
        ModDir = "csgo",
        VideoFileName = "cs2_video.txt",
        TweakPrefix = "cs2",
        FixPrefix = "cs2.fix.",
        ExeTweakSuffix = ".cs2",
        Launch = Cs2Content.Launch,
        Issues = Cs2Content.Issues,
        BuildPreset = Cs2Content.BuildPreset,
        PresetExplanation =
            "Меняет только уже существующие настройки графики в cs2_video.txt: тени, текстуры, шейдеры, частицы, затенение (AO), сглаживание, HDR, V-Sync. " +
            "Динамические тени не выключаются ни в одном пресете: по ним видно противника за углом.",
        FullscreenUsesNoBorder = false,
        UpdateSpaceGb = 25,
        MinRamGb = 8,
        ReflexHint = "В CS2 включите NVIDIA Reflex: Настройки → Видео → Расширенные → NVIDIA Reflex Low Latency → «Вкл». Режим «Вкл + усиление» держит частоты GPU выше — проверяйте замером.",
        AmdAntiLagHint = "В CS2 есть встроенный AMD Anti-Lag 2 (Настройки → Видео, карты RDNA) — включайте его в игре, а не в драйвере.",
        LowEndPresetHint = "«Максимум FPS» и разрешение ниже родного (например 1280×960 или 1600×900)",
        HowToCompareApi = Cs2Content.HowToCompareApi,
    };

    public static IReadOnlyList<GameDefinition> All { get; } = new[] { Dota2, Cs2 };

    public static GameDefinition Get(GameId id) => id == GameId.Cs2 ? Cs2 : Dota2;
}
