using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Vdf;

namespace Dota2Optimizer.Core.Game;

/// <summary>In-app fixes a known issue can offer. Each maps to a button; reversible ones go through the backup journal.</summary>
public enum QuickFix
{
    ResetVideoSettings,
    DisableAutoexec,
    SwitchApi,
    ClearShaderCache,
    VerifyFiles,
    OpenNetworkTab,
    StartMatchMonitor,
    FlushSteamDownloadCache,
}

public sealed record KnownIssue(
    string Id,
    string Symptom,
    string Cause,
    IReadOnlyList<string> Steps,
    IReadOnlyList<QuickFix> Fixes,
    string Evidence);

/// <summary>
/// Symptoms players actually hit, with causes and fixes. Only fixes with a plausible mechanism are listed;
/// where evidence is anecdotal the entry says so.
/// </summary>
public static class KnownIssues
{
    /// <summary>Dota 2 issues (kept as <see cref="All"/> for existing callers). CS2 issues live in <see cref="Cs2Content"/>.</summary>
    public static IReadOnlyList<KnownIssue> All => Dota;

    public static IReadOnlyList<KnownIssue> Dota { get; } = new[]
    {
        new KnownIssue("late-game-fps",
            "FPS падает к концу матча или от матча к матчу",
            "Чаще всего это не «игра тормозит со временем», а одно из четырёх: нагрев и троттлинг (особенно ноутбуки), заканчивается видеопамять или ОЗУ, " +
            "растёт потребление памяти самой игрой, или игра постепенно деградирует от матча к матчу без перезапуска. В поздних драках к этому добавляется много юнитов и эффектов — это нагрузка на процессор.",
            new[]
            {
                "Запустите «Монитор матча» на всю игру — в конце он покажет FPS по минутам и назовёт причину просадки (троттлинг, видеопамять, ОЗУ, рост памяти игры).",
                "Ограничьте FPS (+fps_max) под частоту монитора: меньше нагрев → нет троттлинга к 40-й минуте. На ноутбуке — только от сети.",
                "Перезапускайте Dota 2 каждые 2–3 матча: независимое расследование (PimpmuckL) показало, что игра работает хуже с каждой следующей катой без перезапуска.",
                "Если видеопамять заполняется — снизьте качество текстур; если ОЗУ — закройте браузер и включите автоматический файл подкачки.",
                "Пресет «Максимум FPS» снижает нагрузку от эффектов в массовых драках.",
            },
            new[] { QuickFix.StartMatchMonitor },
            "Деградация от матча к матчу — расследование PimpmuckL (PCGamesN); рост памяти — открытые обращения в репозитории Valve на GitHub (macOS/Linux). Волшебной консольной команды против этого нет."),

        new KnownIssue("stutter-after-update",
            "Фризы и рывки после обновления игры или драйвера",
            "Шейдеры компилируются заново: кеш драйвера и Steam устарел. Особенно заметно на Vulkan в первых матчах.",
            new[]
            {
                "Очистите кеш шейдеров (кнопка ниже) и сыграйте 1–2 матча или демо героя — кеш соберётся заново.",
                "В Steam: Настройки → Загрузки (или «Кеш шейдеров») → включите предварительное кеширование шейдеров и «Обработку шейдеров Vulkan в фоне».",
                "На HDD фризы при первом появлении героев и способностей сильнее — перенос игры на SSD помогает больше любых настроек.",
            },
            new[] { QuickFix.ClearShaderCache },
            "Valve сама предупреждала о фризах Vulkan при первом запуске, пока строится кеш шейдеров."),

        new KnownIssue("microstutter",
            "Регулярные микрофризы во время игры",
            "Фоновые программы (RGB-софт, запись Game Bar, оверлеи, синхронизация облака), энергосбережение, а на Windows 11 24H2 — иногда «Оптимизация для оконных игр».",
            new[]
            {
                "Шаг «Диагноз» покажет фоновые программы — закройте RGB-софт, запись и загрузки.",
                "Выключите фоновую запись Game Bar и включите план «Высокая производительность» (твики в плане).",
                "Играете в «окне без рамки» на Windows 11 24H2? Попробуйте переключить «Оптимизацию для оконных игр» (твик в плане, помечен «спорно») или перейти в эксклюзивный полноэкранный режим.",
                "Отключите оверлеи (Discord, MSI Afterburner/RTSS) и проверьте снова.",
            },
            Array.Empty<QuickFix>(),
            "Связь с 24H2 — сообщения пользователей в Microsoft Community; эффект зависит от системы."),

        new KnownIssue("black-screen",
            "Чёрный экран или вылет при запуске",
            "Несовместимые настройки графики (разрешение/частота, которых нет у монитора), сломанный video.txt, проблемы драйвера с выбранным API, команды в autoexec.cfg.",
            new[]
            {
                "Сбросьте настройки графики Dota 2 — игра создаст новые при запуске (копия старых сохраняется, можно отменить).",
                "Переключите API: DX11 ↔ Vulkan.",
                "Временно отключите autoexec.cfg — если помогло, проблема в одной из команд.",
                "Проверьте целостность файлов и обновите драйвер. Посмотрите вкладку «Диагностика вылетов».",
            },
            new[] { QuickFix.ResetVideoSettings, QuickFix.SwitchApi, QuickFix.DisableAutoexec, QuickFix.VerifyFiles },
            "Стандартная последовательность диагностики; все действия обратимы, кроме проверки файлов (она только докачивает)."),

        new KnownIssue("wrong-resolution",
            "Игра открывается не в том разрешении, за пределами экрана или на другом мониторе",
            "В video.txt осталось разрешение/монитор от другой конфигурации (сменили монитор, подключили ТВ, облако Steam принесло настройки с другого ПК).",
            new[]
            {
                "Сбросьте настройки графики — игра определит монитор заново.",
                "Если используется Steam Cloud, после сброса задайте разрешение в самой игре, чтобы облако сохранило правильное.",
            },
            new[] { QuickFix.ResetVideoSettings },
            "Типичная причина — устаревшие ключи setting.defaultres / setting.defaultresheight."),

        new KnownIssue("crash-in-game",
            "Вылеты посреди матча",
            "Чаще всего — драйвер/разгон видеокарты (TDR), оверлеи, нехватка памяти, повреждённые файлы.",
            new[]
            {
                "Вкладка «Диагностика вылетов» → «Анализировать»: она назовёт модуль сбоя и причину.",
                "Сбросьте разгон и андервольт CPU/GPU/памяти до настроек по умолчанию и проверьте снова.",
                "Переключите API (DX11 ↔ Vulkan) и обновите драйвер.",
            },
            new[] { QuickFix.SwitchApi, QuickFix.VerifyFiles },
            "Основано на анализе журнала Windows."),

        new KnownIssue("lag-ping",
            "Лаги, «телепорты», скачки пинга",
            "Wi-Fi, фоновые загрузки, проблемы провайдера или маршрута до серверов Valve. Это сеть, а не FPS.",
            new[]
            {
                "Вкладка «Сеть» → «Запустить тест»: покажет, где проблема — у вас, у провайдера или на маршруте.",
                "Выберите ближайший регион в поиске игры, подключите кабель.",
            },
            new[] { QuickFix.OpenNetworkTab },
            "Dota 2 работает через сеть релеев Valve (SDR); правки TCP на неё не влияют."),

        new KnownIssue("update-stuck",
            "Обновление Dota 2 не скачивается или зависло",
            "Повреждённый кеш загрузки Steam или мало места на диске.",
            new[]
            {
                "Проверьте свободное место (обновлению нужно 10–20 ГБ во время установки).",
                "Очистите кеш загрузки Steam (Steam перезапустится и может попросить войти).",
            },
            new[] { QuickFix.FlushSteamDownloadCache },
            "Штатная функция Steam."),
    };
}

public sealed record DiskUsageItem(string Name, string Path, long SizeBytes, string Note);

/// <summary>Reversible game-side fixes, planned as normal actions so they get backups and an inline undo.</summary>
public static class QuickFixes
{
    public static string ResetVideoId(GameDefinition game) => game.FixPrefix + "reset-video";

    public static string DisableAutoexecId(GameDefinition game) => game.FixPrefix + "disable-autoexec";

    public static string SwitchApiId(GameDefinition game) => game.FixPrefix + "switch-api";

    /// <summary>Deletes every copy of the video config (game folder and Steam Cloud) so the game recreates defaults. Backups make it undoable.</summary>
    public static IReadOnlyList<PlannedAction> PlanResetVideo(GameDefinition game, SteamInfo steam) =>
        steam.VideoConfigPaths.Select(p => PlannedAction.DeleteFile(p, $"Сброс настроек графики: {game.VideoFileName} будет создан игрой заново")).ToList();

    public static string? AutoexecPath(SteamInfo steam) =>
        steam.GameCfgDir is null ? null : Path.Combine(steam.GameCfgDir, "autoexec.cfg");

    /// <summary>Renames autoexec.cfg to autoexec.cfg.disabled (as copy + delete, both journaled).</summary>
    public static IReadOnlyList<PlannedAction> PlanDisableAutoexec(IFileSystem fs, SteamInfo steam)
    {
        var path = AutoexecPath(steam);
        if (path is null || !fs.FileExists(path))
        {
            return Array.Empty<PlannedAction>();
        }

        return new[]
        {
            PlannedAction.WriteFile(path + ".disabled", fs.ReadAllText(path), "Копия autoexec.cfg → autoexec.cfg.disabled"),
            PlannedAction.DeleteFile(path, "Временно отключить autoexec.cfg"),
        };
    }

    /// <summary>Current API forced in launch options: the Vulkan token, the DX11 token or null (game default, DX11 on Windows).</summary>
    public static string? CurrentApi(string? launchOptions, LaunchCatalog catalog)
    {
        var managed = LaunchOptionsComposer.Analyze(launchOptions, catalog).ManagedTokens;
        return managed.FirstOrDefault(t => t.Equals(catalog.VulkanToken, StringComparison.OrdinalIgnoreCase)) ??
               (catalog.Dx11Token is null ? null : managed.FirstOrDefault(t => t.Equals(catalog.Dx11Token, StringComparison.OrdinalIgnoreCase)));
    }

    public static string? CurrentApi(string? launchOptions) => CurrentApi(launchOptions, LaunchOptionCatalog.Dota);

    /// <summary>Launch line with the other API, keeping everything else.</summary>
    public static string SwitchApi(string? launchOptions, LaunchCatalog catalog)
    {
        var onVulkan = CurrentApi(launchOptions, catalog) == catalog.VulkanToken;
        var analysis = LaunchOptionsComposer.Analyze(launchOptions, catalog);
        var tokens = analysis.ManagedTokens
            .Where(t => !t.Equals(catalog.VulkanToken, StringComparison.OrdinalIgnoreCase) &&
                        (catalog.Dx11Token is null || !t.Equals(catalog.Dx11Token, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (!onVulkan)
        {
            tokens.Add(catalog.VulkanToken);
        }
        else if (catalog.Dx11Token is not null)
        {
            tokens.Add(catalog.Dx11Token);
        }

        return string.Join(' ', tokens.Concat(analysis.MythTokens.Select(m => m.Token)).Concat(analysis.UserTokens));
    }

    public static string SwitchApi(string? launchOptions) => SwitchApi(launchOptions, LaunchOptionCatalog.Dota);

    /// <returns>Actions, the new line, and a reason if the change can't be written right now.</returns>
    public static (IReadOnlyList<PlannedAction> Actions, string NewLine, string? Blocker) PlanSwitchApi(GameDefinition game, IFileSystem fs, SteamInfo steam)
    {
        if (steam.LocalConfigPath is null)
        {
            return (Array.Empty<PlannedAction>(), game.Launch.VulkanToken, "Не найден localconfig.vdf — задайте параметр запуска вручную в Steam.");
        }

        string text;
        string? current;
        try
        {
            text = fs.ReadAllText(steam.LocalConfigPath);
            current = LocalConfigEditor.GetLaunchOptions(text, game.AppId);
        }
        catch (VdfParseException ex)
        {
            return (Array.Empty<PlannedAction>(), game.Launch.VulkanToken, $"Не удалось разобрать localconfig.vdf: {ex.Message}");
        }

        var line = SwitchApi(current, game.Launch);
        if (steam.SteamRunning)
        {
            return (Array.Empty<PlannedAction>(), line, "Steam запущен — закройте его полностью или вставьте строку в параметры запуска вручную.");
        }

        var action = PlannedAction.WriteFile(steam.LocalConfigPath, LocalConfigEditor.SetLaunchOptions(text, line, game.AppId), $"Смена API: «{current}» → «{line}»");
        return (new[] { action }, line, null);
    }

    /// <summary>Space taken by things the game accumulates. Shown only; deleting replays or workshop items is up to the user.</summary>
    public static IReadOnlyList<DiskUsageItem> DiskUsage(GameDefinition game, SteamInfo steam, Func<string, long> dirSize)
    {
        var list = new List<DiskUsageItem>();
        if (steam.GameInstallDir is not null)
        {
            var replays = Path.Combine(steam.GameInstallDir, "game", game.ModDir, "replays");
            list.Add(new DiskUsageItem("Повторы и демки (.dem)", replays, dirSize(replays), "Скачанные повторы матчей. Старые можно удалить вручную — их можно скачать снова, пока они доступны на серверах."));
        }

        foreach (var lib in steam.Libraries)
        {
            var workshop = Path.Combine(lib, "steamapps", "workshop", "content", game.AppId);
            var size = dirSize(workshop);
            if (size > 0)
            {
                list.Add(new DiskUsageItem(game.Id == GameId.Cs2 ? "Карты и предметы из Мастерской" : "Пользовательские игры (Workshop)", workshop, size,
                    "Файлы из Steam Workshop. От неиспользуемых можно отписаться в Мастерской — Steam удалит их сам."));
            }
        }

        return list;
    }
}
