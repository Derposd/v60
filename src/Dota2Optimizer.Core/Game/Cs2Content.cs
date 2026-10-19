using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Game;

/// <summary>
/// Counter-Strike 2: launch options, cs2_video.txt presets and known issues. CS2 is a competitive shooter, so
/// presets favour stable frame times and visibility (dynamic shadows stay on) over eye candy.
/// </summary>
public static class Cs2Content
{
    public static readonly LaunchOptionInfo FpsMax = new("+fps_max", "Ограничение FPS",
        "Ограничивает кадры в секунду. Ровный FPS чуть ниже предела, который держит ваш ПК, даёт более стабильную задержку, чем скачущий «без ограничения». " +
        "На ноутбуках и слабых ПК заметно снижает нагрев и троттлинг. 0 — без ограничения.",
        Effectiveness.Medium, Recommended: true, Disputed: false);

    public static readonly LaunchOptionInfo Vulkan = new("-vulkan", "Рендер Vulkan",
        "По умолчанию CS2 на Windows работает на DirectX 11. Vulkan на части систем (чаще на Radeon) даёт выше FPS или ровнее кадры, на других — хуже; " +
        "после обновления драйвера первые матчи возможны фризы компиляции шейдеров. Проверяйте замером.",
        Effectiveness.HardwareDependent, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo Fullscreen = new("-fullscreen", "Запуск в полноэкранном режиме",
        "Принудительно запускает игру в полноэкранном режиме. Полезно, если игра «застряла» в окне. Обычно режим удобнее выбрать в самой игре.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo Console = new("-console", "Консоль разработчика",
        "Открывает консоль (клавиша ~). Нужна для cl_showfps 1, net-графиков и диагностики. На производительность не влияет.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo ConDebug = new("-condebug", "Писать console.log",
        "Сохраняет вывод консоли в game\\csgo\\console.log — по нему вкладка «Вылеты» ищет ошибки. Включайте на время поиска проблемы.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo ExecAutoexec = new("+exec autoexec", "Выполнять autoexec.cfg при запуске",
        "Если у вас есть свой autoexec.cfg (бинды, прицел), этот параметр гарантирует, что он выполнится при старте. На FPS не влияет.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo AllowThirdParty = new("-allow_third_party_software", "Разрешить сторонние программы в игре",
        "CS2 по умолчанию не пускает в игру сторонние модули — из-за этого OBS «Захват игры» и некоторые оверлеи показывают чёрный экран. " +
        "Параметр снимает запрет, но Valve предупреждает, что это может снизить Trust Factor. Для стрима лучше «Захват экрана» в OBS без этого параметра.",
        Effectiveness.Low, Recommended: false, Disputed: true);

    public static readonly LaunchOptionInfo High = new("-high", "Высокий приоритет процесса",
        "Запускает игру с высоким приоритетом. Проверенных тестов с выигрышем в CS2 нет; при нехватке ядер может мешать звуку, Discord и стримингу. Включайте только после своего замера.",
        Effectiveness.Low, Recommended: false, Disputed: true);

    public static IReadOnlyList<LaunchOptionInfo> All { get; } = new[] { FpsMax, Vulkan, Fullscreen, ExecAutoexec, Console, ConDebug, AllowThirdParty, High };

    /// <summary>Options from CS:GO-era guides that do nothing in CS2 (or only hurt). Offered for removal.</summary>
    public static IReadOnlyDictionary<string, string> Myths { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["-novid"] = "в CS2 нет вступительного ролика — параметр ничего не делает",
        ["-threads"] = "движок сам определяет число потоков; ручное значение может только навредить",
        ["-tickrate"] = "действует только на локальном сервере; официальные матчи CS2 используют subtick",
        ["+cl_updaterate"] = "в CS2 с subtick сетевые rate-команды убраны",
        ["+cl_cmdrate"] = "в CS2 с subtick сетевые rate-команды убраны",
        ["+cl_interp"] = "в CS2 интерполяция настраивается автоматически",
        ["+cl_interp_ratio"] = "в CS2 интерполяция настраивается автоматически",
        ["+cl_forcepreload"] = "команда CS:GO, в CS2 не работает",
        ["+mat_queue_mode"] = "команда CS:GO, в CS2 не работает",
        ["+r_dynamic"] = "команда CS:GO, в CS2 не работает",
        ["-nojoy"] = "команда CS:GO; в CS2 подтверждённого эффекта нет",
        ["-d3d9ex"] = "DirectX 9 — это CS:GO, в CS2 его нет",
        ["-dxlevel"] = "параметр DirectX 9 из CS:GO",
        ["-dx9"] = "DirectX 9 — это CS:GO, в CS2 его нет",
        ["-softparticlesdefaultoff"] = "параметр CS:GO, в CS2 не работает",
        ["-limitvsconst"] = "параметр CS:GO, в CS2 не работает",
        ["-forcenovsync"] = "параметр CS:GO; V-Sync выключается в настройках CS2",
        ["-processheap"] = "параметр 32-битного CS:GO, в 64-битном CS2 не нужен",
        ["-heapsize"] = "параметр 32-битного CS:GO, в 64-битном CS2 не нужен",
        ["-noaafonts"] = "параметр CS:GO, в CS2 не работает",
        ["-USEALLAVAILABLECORES"] = "миф: движок и так использует ядра",
        ["-preload"] = "нет подтверждений, что работает в CS2",
        ["-noforcemaccel"] = "параметр мыши из CS:GO; в CS2 используется raw input",
        ["-noforcemparms"] = "параметр мыши из CS:GO; в CS2 используется raw input",
        ["-noforcemspd"] = "параметр мыши из CS:GO; в CS2 используется raw input",
        ["-useforcedmparms"] = "параметр мыши из CS:GO; в CS2 используется raw input",
    };

    public static LaunchCatalog Launch { get; } = new()
    {
        All = All,
        Myths = Myths,
        ValueOptions = new HashSet<string>(new[] { "-threads", "-tickrate", "-dxlevel", "-heapsize" }, StringComparer.OrdinalIgnoreCase),
        FpsMax = FpsMax,
        Dx11Token = null,
    };

    public const string HowToCompareApi =
        "1) Без -vulkan (по умолчанию DX11) зайдите на «Мастерскую» → карта для бенчмарка (например «CS2 FPS Benchmark») или в одиночную игру с ботами на одной карте.\n" +
        "2) Включите счётчик: консоль (нужен -console) → cl_showfps 1, либо Настройки → Игра → Телеметрия → «Показывать FPS».\n" +
        "3) Замерьте 2–3 минуты по одному маршруту: средний FPS и минимум в дыму и в перестрелке.\n" +
        "4) Повторите с -vulkan. Первый запуск после смены API может фризить, пока строится кеш шейдеров — замеряйте со второго захода.\n" +
        "5) Оставьте вариант с более высоким минимальным FPS (1% low), а не только средним.";

    /// <summary>
    /// cs2_video.txt values. Keys come from real files; a key missing from the user's file is skipped.
    /// shadow_quality: 0 low … 3 very high; texture_detail: 0 low … 2 high; r_texturefilteringquality: 0 bilinear … 5 16x;
    /// shaderquality: 0 low, 1 high; particle_detail: 0 low … 3; ao_detail: 0 off … 2; hdr_detail 3 = performance;
    /// msaa_samples: 0/2/4/8, r_csgo_cmaa_enable = CMAA2 when MSAA is off.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildPreset(VideoPreset preset, PcProfile profile, double vramGb)
    {
        var lowVram = vramGb > 0 && vramGb < 3.5;
        var weak = profile == PcProfile.Weak;
        var s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Visibility first: dynamic shadows show players around corners, so they stay on.
            ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.mat_vsync"] = "0",
        };

        switch (preset)
        {
            case VideoPreset.MaxFps:
                s["setting.videocfg_shadow_quality"] = "0";
                s["setting.videocfg_texture_detail"] = weak || lowVram ? "0" : "1";
                s["setting.r_texturefilteringquality"] = weak ? "1" : "3";
                s["setting.shaderquality"] = "0";
                s["setting.videocfg_particle_detail"] = "0";
                s["setting.videocfg_ao_detail"] = "0";
                s["setting.videocfg_hdr_detail"] = "3";
                s["setting.msaa_samples"] = weak ? "0" : "2";
                s["setting.r_csgo_cmaa_enable"] = weak ? "1" : "0";
                break;

            case VideoPreset.Balance:
                s["setting.videocfg_shadow_quality"] = weak ? "0" : "1";
                s["setting.videocfg_texture_detail"] = lowVram ? "0" : vramGb >= 6 ? "2" : "1";
                s["setting.r_texturefilteringquality"] = "4";
                s["setting.shaderquality"] = "0";
                s["setting.videocfg_particle_detail"] = weak ? "0" : "1";
                s["setting.videocfg_ao_detail"] = "0";
                s["setting.videocfg_hdr_detail"] = "3";
                s["setting.msaa_samples"] = profile == PcProfile.Strong ? "4" : "2";
                s["setting.r_csgo_cmaa_enable"] = "0";
                break;

            case VideoPreset.Quality:
                s["setting.videocfg_shadow_quality"] = profile == PcProfile.Strong ? "3" : "2";
                s["setting.videocfg_texture_detail"] = lowVram ? "1" : "2";
                s["setting.r_texturefilteringquality"] = "5";
                s["setting.shaderquality"] = "1";
                s["setting.videocfg_particle_detail"] = profile == PcProfile.Strong ? "3" : "2";
                s["setting.videocfg_ao_detail"] = profile == PcProfile.Strong ? "2" : "1";
                s["setting.msaa_samples"] = profile == PcProfile.Strong ? "8" : "4";
                s["setting.r_csgo_cmaa_enable"] = "0";
                break;
        }

        return s;
    }

    public static IReadOnlyList<KnownIssue> Issues { get; } = new[]
    {
        new KnownIssue("cs2-low-fps",
            "Низкий FPS или просадки в дыму, на молотовах и в перестрелках",
            "CS2 сильно нагружает процессор, а объёмные дымы и эффекты — видеокарту. Просадки именно в замесах обычно значат, что упираетесь в CPU " +
            "(фоновые программы, троттлинг, одноканальная память) или что включены тяжёлые настройки: тени «Очень высокие», AO, MSAA 8x, шейдеры «Высокие».",
            new[]
            {
                "Шаг «План»: пресет «Максимум FPS» или «Баланс» — тени, AO и шейдеры дают больше всего.",
                "Шаг «Диагноз» покажет фоновые программы: браузер, RGB-софт и запись экрана в CS2 заметно режут минимальный FPS.",
                "Запишите матч в «Мониторе матча» — он покажет, упираетесь вы в процессор или видеокарту и нет ли троттлинга.",
                "Ограничьте FPS (+fps_max) чуть ниже уровня, который ПК держит в замесах, — задержка станет ровнее.",
                "На ноутбуке играйте только от сети и с профилем производительности в утилите производителя.",
            },
            new[] { QuickFix.StartMatchMonitor },
            "Профиль нагрузки CS2 — обзоры производительности (Digital Foundry, Hardware Unboxed, 2023–2025); точный прирост зависит от железа."),

        new KnownIssue("cs2-stutter-after-update",
            "Фризы в первых раундах после обновления игры или драйвера",
            "Шейдеры компилируются заново: кеш драйвера и Steam устарел. Сильнее заметно на Vulkan и на HDD.",
            new[]
            {
                "Очистите кеш шейдеров (кнопка ниже) и сыграйте пару раундов с ботами — кеш соберётся заново.",
                "В Steam: Настройки → Загрузки (или «Кеш шейдеров») → включите предварительное кеширование шейдеров.",
                "Если фризы только на -vulkan — вернитесь на DX11 (кнопка «Переключить API»).",
            },
            new[] { QuickFix.ClearShaderCache, QuickFix.SwitchApi },
            "Стандартное поведение Source 2 после обновлений."),

        new KnownIssue("cs2-input-lag",
            "Задержка ввода, «ватная» мышь",
            "Включён V-Sync, тройная буферизация или ограничение FPS через драйвер, игра в оконном режиме, не включён Reflex / Anti-Lag 2.",
            new[]
            {
                "Выключите V-Sync в игре (пресеты программы делают это сами).",
                "NVIDIA: включите NVIDIA Reflex в настройках видео CS2. AMD: включите AMD Anti-Lag 2 там же.",
                "Играйте в полноэкранном режиме (твик «Эксклюзивный полноэкранный режим» в плане).",
                "Ограничивайте FPS через +fps_max, а не через V-Sync.",
                "Монитор с G-Sync/FreeSync: включите его в панели драйвера и ограничьте FPS на 3 ниже частоты.",
            },
            Array.Empty<QuickFix>(),
            "Рекомендации NVIDIA (Reflex) и AMD (Anti-Lag 2), обе функции официально встроены в CS2."),

        new KnownIssue("cs2-microstutter",
            "Регулярные микрофризы во время игры",
            "Фоновые программы (RGB-софт, запись Game Bar, оверлеи, синхронизация облака), энергосбережение, на Windows 11 24H2 — иногда «Оптимизация для оконных игр».",
            new[]
            {
                "Шаг «Диагноз» покажет фоновые программы — закройте RGB-софт, запись и загрузки.",
                "Выключите фоновую запись Game Bar и включите план «Высокая производительность» (твики в плане).",
                "Отключите оверлеи (Discord, MSI Afterburner/RTSS) и проверьте снова.",
            },
            Array.Empty<QuickFix>(),
            "Связь с 24H2 — сообщения пользователей в Microsoft Community; эффект зависит от системы."),

        new KnownIssue("cs2-black-screen",
            "Чёрный экран или вылет при запуске",
            "Несовместимые настройки графики (разрешение/частота, которых нет у монитора), сломанный cs2_video.txt, проблемы драйвера с выбранным API, команды в autoexec.cfg.",
            new[]
            {
                "Сбросьте настройки графики CS2 — игра создаст новые при запуске (копия старых сохраняется, можно отменить).",
                "Переключите API: DX11 ↔ Vulkan.",
                "Временно отключите autoexec.cfg — если помогло, проблема в одной из команд.",
                "Проверьте целостность файлов и обновите драйвер. Посмотрите вкладку «Вылеты».",
            },
            new[] { QuickFix.ResetVideoSettings, QuickFix.SwitchApi, QuickFix.DisableAutoexec, QuickFix.VerifyFiles },
            "Стандартная последовательность диагностики; все действия обратимы, кроме проверки файлов (она только докачивает)."),

        new KnownIssue("cs2-resolution",
            "Не то разрешение, растянутое 4:3 не растягивается, игра на другом мониторе",
            "В cs2_video.txt осталось разрешение или монитор от другой конфигурации. Растянутое 4:3 требует ещё и масштабирования в драйвере видеокарты.",
            new[]
            {
                "Сбросьте настройки графики — игра определит монитор заново, затем выберите разрешение в игре.",
                "Для растянутого 4:3: NVIDIA — Панель управления → Регулировка размера и положения рабочего стола → «Во весь экран», выполнять на «ГП». AMD — Adrenalin → Дисплей → Масштабирование GPU «Вкл», режим «Полная панель».",
                "Используется Steam Cloud? После сброса задайте разрешение в самой игре, чтобы облако сохранило правильное.",
            },
            new[] { QuickFix.ResetVideoSettings },
            "Типичная причина — устаревшие ключи setting.defaultres / setting.defaultresheight."),

        new KnownIssue("cs2-crash",
            "Вылеты посреди матча",
            "Чаще всего — драйвер/разгон видеокарты (TDR), оверлеи, нехватка памяти, повреждённые файлы.",
            new[]
            {
                "Вкладка «Вылеты» → «Анализировать»: она назовёт модуль сбоя и причину.",
                "Сбросьте разгон и андервольт CPU/GPU/памяти до настроек по умолчанию и проверьте снова.",
                "Переключите API (DX11 ↔ Vulkan) и обновите драйвер.",
            },
            new[] { QuickFix.SwitchApi, QuickFix.VerifyFiles },
            "Основано на анализе журнала Windows."),

        new KnownIssue("cs2-vac-verify",
            "«VAC не удалось проверить ваш игровой сеанс»",
            "Steam не смог проверить файлы игры или своё окружение: повреждённые файлы, сбой службы Steam, программы, которые встраиваются в процессы (некоторые оверлеи, «оптимизаторы», антивирусы).",
            new[]
            {
                "Полностью перезапустите Steam (Steam → Выход), затем компьютер.",
                "Проверьте целостность файлов CS2 (кнопка ниже).",
                "Закройте программы, которые встраиваются в игры: сторонние оверлеи, макросы, «бустеры». Сама эта программа в процесс игры не встраивается.",
                "Если не помогло — восстановите службу Steam: Win+R → \"C:\\Program Files (x86)\\Steam\\bin\\SteamService.exe\" /repair (нужны права администратора).",
            },
            new[] { QuickFix.VerifyFiles },
            "Шаги из официальной справки Steam по ошибке VAC."),

        new KnownIssue("cs2-obs-black",
            "OBS или запись показывают чёрный экран вместо игры",
            "CS2 блокирует сторонние модули в процессе игры, а «Захват игры» в OBS работает именно так.",
            new[]
            {
                "В OBS используйте «Захват экрана» или «Захват окна» вместо «Захвата игры» — так ничего не встраивается в CS2.",
                "Параметр -allow_third_party_software снимает запрет, но может снизить Trust Factor — в плане он помечен «спорно» и не включается сам.",
            },
            Array.Empty<QuickFix>(),
            "Поведение описано Valve в обновлении CS2 о защите от стороннего ПО."),

        new KnownIssue("cs2-lag",
            "Лаги, «телепорты», потери пакетов, rubber banding",
            "Wi-Fi, фоновые загрузки, проблемы провайдера или маршрута до серверов Valve. Это сеть, а не FPS.",
            new[]
            {
                "Вкладка «Сеть» → «Запустить тест»: покажет, где проблема — у вас, у провайдера или на маршруте.",
                "В CS2 включите телеметрию: Настройки → Игра → Телеметрия — там видны пинг и потери пакетов.",
                "Подключите кабель вместо Wi-Fi и поставьте на паузу загрузки.",
            },
            new[] { QuickFix.OpenNetworkTab },
            "CS2 работает через сеть релеев Valve (SDR); правки TCP на неё не влияют."),

        new KnownIssue("cs2-update-stuck",
            "Обновление CS2 не скачивается или зависло",
            "Повреждённый кеш загрузки Steam или мало места на диске.",
            new[]
            {
                "Проверьте свободное место (крупному обновлению нужно 20–30 ГБ во время установки).",
                "Очистите кеш загрузки Steam (Steam перезапустится и может попросить войти).",
            },
            new[] { QuickFix.FlushSteamDownloadCache },
            "Штатная функция Steam."),
    };
}
