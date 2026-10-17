using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Vdf;

namespace Dota2Optimizer.Core.Tweaks;

public sealed class VideoPresetTweak : Tweak
{
    public override string Id => "dota.video.preset";
    public override string Title => "Графический пресет Dota 2 (video.txt)";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Effectiveness.High;

    public override string Explanation =>
        "Меняет только уже существующие настройки графики в video.txt: тени, SSAO, туман, отражения, вода, рендер-масштаб, V-Sync. " +
        "Самый заметный способ поднять FPS, особенно на слабых ПК: тени и масштаб рендера — главные потребители видеокарты.";

    public override string Risk => "Минимальный: копия video.txt сохраняется, игра должна быть закрыта";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var steam = ctx.Snapshot.Steam;
        if (!steam.DotaFound)
        {
            return Result(TweakStatus.NotApplicable, "Dota 2 не найдена");
        }

        if (steam.VideoConfigPaths.Count == 0)
        {
            return Result(TweakStatus.ManualOnly, "video.txt не найден. Запустите Dota 2 один раз и измените любую настройку графики — файл появится.");
        }

        if (steam.DotaRunning)
        {
            return Result(TweakStatus.Blocked, "Закройте Dota 2: при выходе игра перезапишет video.txt своими настройками.");
        }

        var settings = VideoPresets.Build(ctx.Game.Preset, ctx.Snapshot.Profile, ctx.Snapshot.PrimaryGpu?.VramGb ?? 0);
        var actions = new List<PlannedAction>();
        var notes = new List<string>();
        foreach (var path in steam.VideoConfigPaths)
        {
            var app = VideoPresets.Apply(ctx.FileSystem.ReadAllText(path), settings);
            if (app.Changes.Count > 0)
            {
                var summary = string.Join(", ", app.Changes.Select(c => $"{c.Key.Replace("setting.", string.Empty)}: {c.From}→{c.To}"));
                actions.Add(PlannedAction.WriteFile(path, app.NewText, $"Пресет «{VideoPresets.Describe(ctx.Game.Preset)}»: {summary}"));
            }

            if (app.MissingKeys.Count > 0)
            {
                notes.Add($"в {PathName(path)} нет ключей ({app.MissingKeys.Count}), они пропущены");
            }
        }

        var cloud = steam.VideoConfigPaths.Any(p => p.Contains("\\remote\\", StringComparison.OrdinalIgnoreCase)) || steam.CloudCfgDirs.Count > 0
            ? " Внимание: часть настроек синхронизируется через Steam Cloud — после запуска игры облако может вернуть старые значения; тогда примените пресет ещё раз или поменяйте настройку в самой игре."
            : string.Empty;

        if (actions.Count == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Настройки уже соответствуют пресету." + cloud);
        }

        var note = notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : string.Empty;
        return Result(TweakStatus.CanApply, $"Будет изменено файлов: {actions.Count}{note}.{cloud}", actions);
    }

    private static string PathName(string path) => path.Contains("\\remote\\", StringComparison.OrdinalIgnoreCase) ? "облачном video.txt" : "video.txt";
}

public sealed class LaunchOptionsTweak : Tweak
{
    public override string Id => "dota.launch.options";
    public override string Title => "Параметры запуска Dota 2";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Effectiveness.Medium;

    public override string Explanation =>
        "Записывает выбранные параметры запуска (-novid, +fps_max, выбор API) в настройки Steam и убирает устаревшие «ускорители» вроде -threads и -prewarm. " +
        "Ваши собственные параметры сохраняются.";

    public override string Risk => "Минимальный: копия localconfig.vdf сохраняется; Steam должен быть закрыт, иначе он перезапишет файл";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var steam = ctx.Snapshot.Steam;
        if (!steam.DotaFound)
        {
            return Result(TweakStatus.NotApplicable, "Dota 2 не найдена");
        }

        string? current = null;
        if (steam.LocalConfigPath is not null)
        {
            try
            {
                current = LocalConfigEditor.GetLaunchOptions(ctx.FileSystem.ReadAllText(steam.LocalConfigPath));
            }
            catch (VdfParseException ex)
            {
                return Result(TweakStatus.ManualOnly, $"Не удалось разобрать localconfig.vdf ({ex.Message}). Скопируйте строку вручную.");
            }
        }

        string line;
        try
        {
            line = LaunchOptionsComposer.Compose(current, ctx.Game.LaunchTokens, ctx.Game.FpsMax, ctx.Game.RemoveMythLaunchOptions);
        }
        catch (ArgumentException ex)
        {
            return Result(TweakStatus.Blocked, ex.Message);
        }

        if (string.Equals(line.Trim(), (current ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            return Result(TweakStatus.AlreadyOptimal, $"Текущая строка: «{current}»", copy: line);
        }

        const string manual = "Steam → Библиотека → Dota 2 → ПКМ → Свойства → Общие → Параметры запуска.";
        if (steam.LocalConfigPath is null)
        {
            return Result(TweakStatus.ManualOnly, $"Не найден localconfig.vdf. Вставьте строку вручную: {manual}", copy: line);
        }

        if (steam.SteamRunning)
        {
            return Result(TweakStatus.Blocked, $"Steam запущен — безопасно записать нельзя. Закройте Steam полностью или вставьте строку вручную: {manual}", copy: line);
        }

        var text = LocalConfigEditor.SetLaunchOptions(ctx.FileSystem.ReadAllText(steam.LocalConfigPath), line);
        var action = PlannedAction.WriteFile(steam.LocalConfigPath, text, $"Параметры запуска: «{current}» → «{line}»");
        return Result(TweakStatus.CanApply, $"Новая строка: «{line}»", new[] { action }, copy: line);
    }
}

public sealed class ExclusiveFullscreenTweak : Tweak
{
    private const string Fullscreen = "setting.fullscreen";
    private const string NoBorder = "setting.nowindowborder";

    public override string Id => "dota.fullscreen";
    public override string Title => "Эксклюзивный полноэкранный режим Dota 2";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;

    public override string Explanation =>
        "Переключает игру из окна/«окна без рамки» в эксклюзивный полноэкранный режим (те же ключи video.txt, что и в настройках игры). " +
        "Обычно это минимальная задержка и ровные кадры на мониторах 144 Гц+. Минусы: Alt+Tab медленнее, хуже работают оверлеи и запись экрана. " +
        "Если у вас фризы именно в полноэкранном режиме — наоборот, попробуйте «окно без рамки» в самой игре.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var steam = ctx.Snapshot.Steam;
        if (!steam.DotaFound || steam.VideoConfigPaths.Count == 0)
        {
            return Result(TweakStatus.NotApplicable, "video.txt не найден");
        }

        if (steam.DotaRunning)
        {
            return Result(TweakStatus.Blocked, "Закройте Dota 2: при выходе игра перезапишет video.txt.");
        }

        var actions = new List<PlannedAction>();
        foreach (var path in steam.VideoConfigPaths)
        {
            var cfg = VideoConfig.Parse(ctx.FileSystem.ReadAllText(path));
            if (cfg.Get(Fullscreen) is null)
            {
                continue;
            }

            if (cfg.Get(Fullscreen) == "1" && cfg.Get(NoBorder) is null or "0")
            {
                continue;
            }

            cfg.TrySet(Fullscreen, "1");
            cfg.TrySet(NoBorder, "0");
            actions.Add(PlannedAction.WriteFile(path, cfg.ToString(), "Режим экрана Dota 2: эксклюзивный полноэкранный"));
        }

        return actions.Count == 0
            ? Result(TweakStatus.AlreadyOptimal, "Игра уже в эксклюзивном полноэкранном режиме (или ключей режима нет в video.txt).")
            : Result(TweakStatus.CanApply, "Сейчас игра в окне или «окне без рамки».", actions);
    }

    /// <summary>True when Dota runs windowed/borderless according to its video.txt (used by Windows-side tweaks).</summary>
    public static bool? IsWindowed(TweakContext ctx)
    {
        var path = ctx.Snapshot.Steam.VideoConfigPaths.FirstOrDefault();
        if (path is null || !ctx.FileSystem.FileExists(path))
        {
            return null;
        }

        var cfg = VideoConfig.Parse(ctx.FileSystem.ReadAllText(path));
        return cfg.Get(Fullscreen) switch
        {
            null => null,
            "1" => cfg.Get(NoBorder) == "1",
            _ => true,
        };
    }
}
