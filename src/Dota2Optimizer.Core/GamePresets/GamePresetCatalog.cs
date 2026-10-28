using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.GamePresets;

/// <summary>Where a user's game config lives: roots are injected so tests run on any OS.</summary>
public sealed record ConfigRoots(string LocalAppData, string AppData, string UserProfile)
{
    public static ConfigRoots Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}

public sealed record PresetValue(string? Section, string Key, string Value);

public sealed record GamePresetDef(string Id, string Name, string Description, Effectiveness Effect, IReadOnlyList<PresetValue> Values);

public sealed record GameConfigDef(
    string Id,
    string Name,
    string Process,
    ConfigFormat Format,
    Func<ConfigRoots, IFileSystem, IReadOnlyList<string>> Files,
    string Caveat,
    IReadOnlyList<GamePresetDef> Presets);

/// <summary>
/// One preset for one game, as a tweak: the config file is backed up and rewritten with only the preset's existing keys changed.
/// The game must be closed (it rewrites its config on exit). Game memory and files other than the user config are never touched.
/// </summary>
public sealed class GamePresetTweak : Tweak
{
    public GamePresetTweak(GameConfigDef game, GamePresetDef preset, ConfigRoots roots)
    {
        Game = game;
        Preset = preset;
        Roots = roots;
    }

    public GameConfigDef Game { get; }
    public GamePresetDef Preset { get; }
    public ConfigRoots Roots { get; }

    /// <summary>Running-process check; replaced in tests.</summary>
    public static Func<string, bool> IsRunning { get; set; } = SystemInfo.SystemScanner.IsProcessRunning;

    public override string Id => $"preset:{Game.Id}:{Preset.Id}";
    public override string Title => $"{Game.Name}: {Preset.Name}";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Preset.Effect;
    public override string Explanation => Preset.Description;
    public override string AppliesTo => Game.Caveat;
    public override string Risk => "Минимальный: меняются только значения в файле настроек игры, копия файла сохраняется и возвращается выключением";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var files = Game.Files(Roots, ctx.FileSystem);
        if (files.Count == 0)
        {
            return Result(TweakStatus.NotApplicable, $"Файл настроек {Game.Name} не найден. Запустите игру хотя бы раз, чтобы она его создала.", recommendedHere: false);
        }

        if (IsRunning(Game.Process))
        {
            return Result(TweakStatus.Blocked, $"Закройте {Game.Name}: при выходе игра перезапишет файл настроек.", recommendedHere: false);
        }

        var actions = new List<PlannedAction>();
        var changed = 0;
        var found = 0;
        foreach (var file in files)
        {
            var text = ctx.FileSystem.ReadAllText(file);
            var cfg = ConfigText.Parse(Game.Format, text);
            foreach (var v in Preset.Values)
            {
                var current = cfg.Get(v.Section, v.Key);
                if (current is null)
                {
                    continue;
                }

                found++;
                if (!Same(current, v.Value) && cfg.TrySet(v.Section, v.Key, v.Value))
                {
                    changed++;
                }
            }

            var result = cfg.ToString();
            if (result != text)
            {
                actions.Add(PlannedAction.WriteFile(file, result, $"{Game.Name}: «{Preset.Name}» в {PathEx.FileName(file)}"));
            }
        }

        if (found == 0)
        {
            return Result(TweakStatus.NotApplicable, "В файле настроек нет ни одного известного ключа — версия игры могла сменить формат. Файл не трогаем.", recommendedHere: false);
        }

        return actions.Count == 0
            ? Result(TweakStatus.AlreadyOptimal, "Настройки уже такие.", recommendedHere: false)
            : Result(TweakStatus.CanApply, $"Изменится значений: {changed} (из {found} найденных ключей пресета).", actions, recommendedHere: false);
    }

    private static bool Same(string current, string wanted)
    {
        var c = current.Trim().Trim('"');
        if (double.TryParse(c, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(wanted, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
        {
            return Math.Abs(a - b) < 1e-6;
        }

        return string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase);
    }
}

public static class GamePresetCatalog
{
    private const string Sg = "ScalabilityGroups";
    private const string FortSettings = "/Script/FortniteGame.FortGameUserSettings";

    private static PresetValue S(string key, string value) => new(Sg, key, value);

    private static IReadOnlyList<string> Existing(IFileSystem fs, params string[] paths) => paths.Where(fs.FileExists).ToList();

    public static IReadOnlyList<GameConfigDef> All { get; } = new[]
    {
        new GameConfigDef("fortnite", "Fortnite", "FortniteClient-Win64-Shipping", ConfigFormat.Ini,
            (r, fs) => Existing(fs, Path.Combine(r.LocalAppData, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini")),
            "Fortnite синхронизирует часть настроек с аккаунтом Epic: если игра вернула старые значения, примените пресет ещё раз или выставьте в меню. Античит EAC файл настроек не проверяет.",
            new[]
            {
                new GamePresetDef("perf", "Максимум FPS", "Низкие тени, эффекты, постобработка и листва, сглаживание выкл., дальность средняя, текстуры средние, без размытия и V-Sync. Разрешение рендера не меняется.",
                    Effectiveness.High, new[]
                    {
                        S("sg.ViewDistanceQuality", "1"), S("sg.AntiAliasingQuality", "0"), S("sg.ShadowQuality", "0"), S("sg.GlobalIlluminationQuality", "0"),
                        S("sg.ReflectionQuality", "0"), S("sg.PostProcessQuality", "0"), S("sg.TextureQuality", "1"), S("sg.EffectsQuality", "0"),
                        S("sg.FoliageQuality", "0"), S("sg.ShadingQuality", "0"),
                        new PresetValue(FortSettings, "bMotionBlur", "False"), new PresetValue(FortSettings, "bUseVSync", "False"),
                    }),
                new GamePresetDef("balance", "Баланс", "Средние настройки, тени низкие (самая дорогая настройка), без размытия и V-Sync.", Effectiveness.Medium, new[]
                {
                    S("sg.ViewDistanceQuality", "2"), S("sg.AntiAliasingQuality", "1"), S("sg.ShadowQuality", "0"), S("sg.GlobalIlluminationQuality", "1"),
                    S("sg.ReflectionQuality", "1"), S("sg.PostProcessQuality", "1"), S("sg.TextureQuality", "2"), S("sg.EffectsQuality", "1"),
                    S("sg.FoliageQuality", "1"), S("sg.ShadingQuality", "1"),
                    new PresetValue(FortSettings, "bMotionBlur", "False"), new PresetValue(FortSettings, "bUseVSync", "False"),
                }),
            }),
        new GameConfigDef("valorant", "Valorant", "VALORANT-Win64-Shipping", ConfigFormat.Ini,
            (r, fs) =>
            {
                var root = Path.Combine(r.LocalAppData, "VALORANT", "Saved", "Config");
                return fs.EnumerateDirectories(root).Select(d => Path.Combine(d, "Windows", "GameUserSettings.ini")).Where(fs.FileExists).ToList();
            },
            "Честно: Valorant хранит основные настройки графики на сервере Riot и может вернуть их при входе — тогда выставьте их в меню игры. Vanguard файл настроек не проверяет, программа в процесс игры не вмешивается.",
            new[]
            {
                new GamePresetDef("perf", "Максимум FPS", "Все группы качества на минимум. Valorant и так лёгкий: на слабых ПК и ноутбуках разница заметна, на мощных — почти нет.",
                    Effectiveness.Medium, new[]
                    {
                        S("sg.ViewDistanceQuality", "0"), S("sg.AntiAliasingQuality", "0"), S("sg.ShadowQuality", "0"), S("sg.PostProcessQuality", "0"),
                        S("sg.TextureQuality", "0"), S("sg.EffectsQuality", "0"), S("sg.FoliageQuality", "0"), S("sg.ShadingQuality", "0"),
                    }),
            }),
        new GameConfigDef("minecraft", "Minecraft (Java)", "javaw", ConfigFormat.Colon,
            (r, fs) => Existing(fs, Path.Combine(r.AppData, ".minecraft", "options.txt")),
            "Только Minecraft Java Edition (официальный лаунчер). Для модпаков с Sodium/OptiFine у модов свои настройки — они важнее этих.",
            new[]
            {
                new GamePresetDef("perf", "Максимум FPS", "Прорисовка 8 чанков, симуляция 6, графика «Быстро», минимум частиц, без теней сущностей, облаков, мягкого освещения и V-Sync, без смешивания биомов.",
                    Effectiveness.High, new[]
                    {
                        new PresetValue(null, "renderDistance", "8"), new PresetValue(null, "simulationDistance", "6"), new PresetValue(null, "graphicsMode", "0"),
                        new PresetValue(null, "particles", "2"), new PresetValue(null, "entityShadows", "false"), new PresetValue(null, "renderClouds", "false"),
                        new PresetValue(null, "ao", "false"), new PresetValue(null, "enableVsync", "false"), new PresetValue(null, "biomeBlendRadius", "0"),
                        new PresetValue(null, "entityDistanceScaling", "0.75"),
                    }),
                new GamePresetDef("balance", "Баланс", "Прорисовка 12 чанков, графика «Детально», облака «Быстро», без V-Sync.", Effectiveness.Medium, new[]
                {
                    new PresetValue(null, "renderDistance", "12"), new PresetValue(null, "simulationDistance", "8"), new PresetValue(null, "graphicsMode", "1"),
                    new PresetValue(null, "particles", "1"), new PresetValue(null, "renderClouds", "fast"), new PresetValue(null, "enableVsync", "false"),
                    new PresetValue(null, "biomeBlendRadius", "1"),
                }),
            }),
        new GameConfigDef("apex", "Apex Legends", "r5apex", ConfigFormat.SourceVideo,
            (r, fs) => Existing(fs, Path.Combine(r.UserProfile, "Saved Games", "Respawn", "Apex", "local", "videoconfig.txt")),
            "Меняется только videoconfig.txt в «Сохранённые игры». Античит EAC его не проверяет; если игра откроет настройки и сохранит их, значения из меню перезапишут пресет.",
            new[]
            {
                new GamePresetDef("perf", "Максимум FPS", "Без теней от солнца и динамических теней, SSAO, объёмного света, декалей и сглаживания; меньше частиц и рэгдоллов; V-Sync выкл. Разрешение не меняется.",
                    Effectiveness.High, new[]
                    {
                        new PresetValue(null, "setting.mat_antialias_mode", "0"), new PresetValue(null, "setting.csm_enabled", "0"),
                        new PresetValue(null, "setting.shadow_enable", "0"), new PresetValue(null, "setting.ssao_enabled", "0"),
                        new PresetValue(null, "setting.volumetric_lighting", "0"), new PresetValue(null, "setting.r_decals", "0"),
                        new PresetValue(null, "setting.particle_cpu_level", "0"), new PresetValue(null, "setting.cl_ragdoll_maxcount", "0"),
                        new PresetValue(null, "setting.mat_vsync_mode", "0"), new PresetValue(null, "setting.r_lod_switch_scale", "0.5"),
                    }),
            }),
    };

    public static IReadOnlyList<GamePresetTweak> Tweaks(ConfigRoots roots) =>
        All.SelectMany(g => g.Presets.Select(p => new GamePresetTweak(g, p, roots))).ToList();
}
