using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>HAGS as a plain switch: on = hardware scheduling. The wizard keeps its own "flip" version.</summary>
public sealed class HagsSwitchTweak : Tweak
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Name = "HwSchMode";

    public override string Id => "win.hags.on";
    public override string Title => "Аппаратное планирование GPU (HAGS)";
    public override TweakCategory Category => TweakCategory.Gpu;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool RequiresRestart => true;
    public override string Risk => "Низкий; нужна перезагрузка, откат тоже после перезагрузки. Нужны права администратора";
    public override string AppliesTo => "Обязателен для DLSS Frame Generation (RTX 40/50) и полезен для FSR 3 Frame Generation; в остальных играх разница около нуля";

    public override string Explanation =>
        "Видеокарта сама управляет очередью своих задач вместо процессора. В независимых тестах FPS почти не меняется, " +
        "но без HAGS не работает генерация кадров DLSS 3/4. Если после включения появились фризы — выключите обратно.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var gpu = ctx.Snapshot.PrimaryGpu;
        var supported = gpu is not null && gpu.Vendor switch
        {
            GpuVendor.Nvidia => !gpu.IsIntegrated && !gpu.Name.Contains("GTX 9", StringComparison.OrdinalIgnoreCase),
            GpuVendor.Amd => !gpu.IsIntegrated,
            GpuVendor.Intel => gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        if (!supported || ctx.Snapshot.Windows is { Build: < 19041 })
        {
            return Result(TweakStatus.NotApplicable, "Эта видеокарта или версия Windows не поддерживает HAGS.", recommendedHere: false);
        }

        if (ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt() == 2)
        {
            return Result(TweakStatus.AlreadyOptimal, "Включён.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Выключен или по умолчанию драйвера.", new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(2), "Включить HAGS (после перезагрузки)"),
        }, recommendedHere: false);
    }
}

/// <summary>
/// The hidden «Максимальная производительность» (Ultimate Performance) plan. If it isn't in the list it is created with a
/// GUID chosen in advance, so undo can delete exactly that copy.
/// </summary>
public sealed class UltimatePlanTweak : Tweak
{
    public override string Id => "win.powerplan.ultimate";
    public override string Title => "План питания «Максимальная производительность»";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override TweakRisk RiskLevel => TweakRisk.Low;
    public override string Risk => "Низкий: выше расход энергии и нагрев в простое. Откат возвращает прежний план и удаляет созданную копию";
    public override string AppliesTo => "Настольные ПК; заметной разницы с «Высокой производительностью» обычно нет";

    public override string Explanation =>
        "Скрытый план Windows: процессор и устройства не уходят в энергосбережение даже на долю секунды. По сравнению с «Высокой производительностью» " +
        "в играх разница обычно в пределах погрешности; на ноутбуке от батареи он только тратит заряд. Оставлен для тех, кто хочет максимум и проверит замером.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var plan = ctx.Snapshot.PowerPlan;
        if (plan is null)
        {
            return Result(TweakStatus.NotApplicable, "Не удалось прочитать план питания.", recommendedHere: false);
        }

        if (plan.ActiveGuid == PowerPlanTweak.UltimatePerformance || plan.ActiveName.Contains("Максимальная", StringComparison.OrdinalIgnoreCase) ||
            plan.ActiveName.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
        {
            return Result(TweakStatus.AlreadyOptimal, $"Активен «{plan.ActiveName}».", recommendedHere: false);
        }

        var existing = plan.Available.FirstOrDefault(p => p.Guid == PowerPlanTweak.UltimatePerformance ||
                                                           p.Name.Contains("Максимальная", StringComparison.OrdinalIgnoreCase) ||
                                                           p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
        var actions = new List<PlannedAction>();
        Guid target;
        if (existing.Guid != Guid.Empty)
        {
            target = existing.Guid;
        }
        else
        {
            target = Guid.NewGuid();
            actions.Add(PlannedAction.Run("powercfg", $"/duplicatescheme {PowerPlanTweak.UltimatePerformance} {target}", "powercfg", $"/delete {target}",
                "Создать план «Максимальная производительность»", requiresAdmin: false));
        }

        actions.Add(PlannedAction.Run("powercfg", $"/setactive {target}", "powercfg", $"/setactive {plan.ActiveGuid}",
            $"План питания: «{plan.ActiveName}» → «Максимальная производительность»", requiresAdmin: false));
        var note = ctx.Snapshot.IsLaptop ? " Это ноутбук: от батареи план сильно сокращает время работы." : string.Empty;
        return Result(TweakStatus.CanApply, $"Сейчас: «{plan.ActiveName}».{note}", actions, recommendedHere: false);
    }
}

/// <summary>Windows tweaks of part A1 (system) and A3 (privacy, interface, services) shown as switches on the «Твики Windows» page.</summary>
public static class SystemTweakCatalog
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static readonly Lazy<IReadOnlyList<Tweak>> Items = new(Create);

    public static IReadOnlyList<Tweak> All => Items.Value;

    public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static string? OnlyWindows11(SystemSnapshot s) => s.Windows is { IsWindows11: false } ? "Есть только в Windows 11." : null;

    private static string? OnlyWindows10(SystemSnapshot s) => s.Windows is { IsWindows11: true } ? "Только для Windows 10." : null;

    private static RegSetting Cu(string key, string name, int value, string text, int? def = null) =>
        new(RegHive.CurrentUser, key, name, RegValue.Dword(value), text, def is null ? null : RegValue.Dword(def.Value));

    private static RegSetting Lm(string key, string name, int value, string text, int? def = null) =>
        new(RegHive.LocalMachine, key, name, RegValue.Dword(value), text, def is null ? null : RegValue.Dword(def.Value));

    private static IReadOnlyList<Tweak> Create() => new Tweak[]
    {
        // ---------- A1: system ----------
        new GameModeTweak(),
        new GameDvrTweak(),
        new RegistryTweak
        {
            TweakId = "win.gamebar.capture",
            Name = "Отключить захват Xbox Game Bar полностью",
            Group = TweakCategory.Windows,
            Effect = Effectiveness.Low,
            Games = "Все игры; заметно на слабых видеокартах и ноутбуках",
            Description = "Выключает запись и скриншоты Game Bar целиком (не только «Записать последние моменты»). Сам Game Bar (Win+G), " +
                          "счётчик FPS и чат Xbox остаются. Если записываете клипы через Game Bar — не включайте; для записи есть OBS, ShadowPlay, ReLive.",
            Settings = new[]
            {
                Cu(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, "Захват Game Bar: выкл.", def: 1),
                Cu(@"System\GameConfigStore", "GameDVR_Enabled", 0, "Game DVR: выкл.", def: 1),
            },
        },
        new HagsSwitchTweak(),
        new PowerPlanTweak(),
        new UltimatePlanTweak(),
        new PowerThrottlingTweak(),
        new MmcssGamesTweak(),
        new RegistryTweak
        {
            TweakId = "win.priosep",
            Name = "Win32PrioritySeparation: короткие кванты для активного окна",
            Group = TweakCategory.Windows,
            Effect = Effectiveness.Low,
            IsDisputed = true,
            Games = "Эффект минимальный; встречаются отзывы о более ровном времени кадра в играх, упирающихся в процессор",
            Description = "Значение 0x26 (38): короткие фиксированные кванты времени процессора и тройной приоритет для окна на переднем плане. " +
                          "По умолчанию Windows уже отдаёт приоритет активному окну (значение 2). Независимых замеров с приростом FPS нет — пункт для проверки на своём ПК.",
            Settings = new[] { Lm(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, "Win32PrioritySeparation = 0x26", def: 2) },
        },
        new AccessibilityHotkeysTweak(),
        new UsbSelectiveSuspendTweak(),
        new GlobalTimerResolutionTweak(),
        new MultiplaneOverlayTweak(),
        new DeliveryOptimizationTweak(),
        new MemoryIntegrityTweak(),

        // ---------- A1: visual effects ----------
        new RegistryTweak
        {
            TweakId = "ui.visualfx",
            Name = "Визуальные эффекты: наилучшее быстродействие",
            Group = TweakCategory.Interface,
            Effect = Effectiveness.Low,
            NeedsRestart = true,
            Games = "На FPS в играх почти не влияет; разгружает рабочий стол на слабых ПК и встроенной графике",
            Description = "То же, что «Свойства системы → Быстродействие → Обеспечить наилучшее быстродействие», но со сглаживанием шрифтов: " +
                          "без теней, анимации меню и плавной прокрутки списков. Вступает в силу после выхода из системы.",
            Settings = new[]
            {
                Cu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2, "Визуальные эффекты: быстродействие", def: 0),
                new RegSetting(RegHive.CurrentUser, @"Control Panel\Desktop", "UserPreferencesMask", new RegValue(RegKind.Binary, "9012038010000000"), "Маска эффектов: без анимаций и теней"),
                new RegSetting(RegHive.CurrentUser, @"Control Panel\Desktop", "FontSmoothing", RegValue.Str("2"), "Сглаживание шрифтов: оставить", RegValue.Str("2")),
                Cu(Advanced, "ListviewShadow", 0, "Тени под подписями значков: выкл.", def: 1),
            },
        },
        new RegistryTweak
        {
            TweakId = "ui.animations",
            Name = "Отключить анимацию окон и панели задач",
            Group = TweakCategory.Interface,
            Effect = Effectiveness.Low,
            NeedsRestart = true,
            Games = "Не влияет на FPS; Alt+Tab и сворачивание игры происходят без задержки на анимацию",
            Description = "Окна сворачиваются и разворачиваются мгновенно, без анимации. Полезно при частом Alt+Tab из игры. После выхода из системы.",
            Settings = new[]
            {
                new RegSetting(RegHive.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegValue.Str("0"), "Анимация сворачивания окон: выкл.", RegValue.Str("1")),
                Cu(Advanced, "TaskbarAnimations", 0, "Анимации панели задач: выкл.", def: 1),
            },
        },
        new RegistryTweak
        {
            TweakId = "ui.transparency",
            Name = "Отключить прозрачность (эффект «акрила»)",
            Group = TweakCategory.Interface,
            Effect = Effectiveness.Low,
            Games = "Не влияет на FPS в полноэкранной игре; на встроенной графике разгружает рабочий стол",
            Description = "Меню «Пуск», панель задач и окна Параметров становятся непрозрачными. Размытие за ними считает видеокарта постоянно, пока они видны.",
            Settings = new[] { Cu(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, "Прозрачность: выкл.", def: 1) },
        },

        // ---------- A3: privacy and clean-up ----------
        new RegistryTweak
        {
            TweakId = "priv.websearch",
            Name = "Убрать веб-поиск Bing из меню «Пуск»",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            IsRecommended = true,
            Games = "Не влияет на FPS",
            Description = "Поиск в «Пуске» ищет только на компьютере: не отправляет набранный текст в Bing и не показывает веб-подсказки. Быстрее открывается результат.",
            Settings = new[]
            {
                Cu(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, "Веб-подсказки в поиске: выкл."),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, "Поиск Bing: выкл.", def: 1),
            },
        },
        new RegistryTweak
        {
            TweakId = "priv.widgets",
            Name = "Отключить виджеты и ленту новостей",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            Games = "Освобождает 100–300 МБ памяти, которые держит процесс виджетов; на 8 ГБ ОЗУ это заметнее",
            Description = "Убирает кнопку виджетов (Windows 11) или «Новости и интересы» (Windows 10) и не даёт им работать в фоне. Через официальную политику Windows.",
            Settings = new[]
            {
                Lm(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, "Виджеты: выкл. (политика)"),
                Lm(@"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0, "Новости и интересы: выкл. (политика)"),
            },
        },
        new RegistryTweak
        {
            TweakId = "priv.copilot",
            Name = "Отключить Copilot",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            NotApplicableWhen = OnlyWindows11,
            Games = "Не влияет на FPS",
            Description = "Убирает кнопку Copilot с панели задач и выключает встроенный Copilot политикой Windows. " +
                          "Если Copilot стоит как отдельное приложение (новые сборки), его удаляют в «Параметры → Приложения».",
            Settings = new[]
            {
                Cu(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, "Copilot: выкл. (политика)"),
                Cu(Advanced, "ShowCopilotButton", 0, "Кнопка Copilot: скрыть", def: 1),
            },
        },
        new RegistryTweak
        {
            TweakId = "priv.cortana",
            Name = "Отключить Cortana",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            NotApplicableWhen = OnlyWindows10,
            Games = "Не влияет на FPS",
            Description = "Политика Windows 10, которая выключает Cortana. Поиск по компьютеру продолжает работать.",
            Settings = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, "Cortana: выкл. (политика)") },
        },
        new RegistryTweak
        {
            TweakId = "priv.adid",
            Name = "Отключить рекламный идентификатор",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            IsRecommended = true,
            Games = "Не влияет на FPS",
            Description = "Приложения перестают получать идентификатор для персональной рекламы. То же, что «Конфиденциальность → Общие → Разрешить приложениям показывать персонализированную рекламу».",
            Settings = new[] { Cu(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, "Рекламный ID: выкл.", def: 1) },
        },
        new RegistryTweak
        {
            TweakId = "priv.tips",
            Name = "Отключить советы, рекомендации и автоустановку рекламных приложений",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            IsRecommended = true,
            Games = "Не влияет на FPS; меньше всплывающих уведомлений поверх игры",
            Description = "Windows перестаёт показывать «советы», предложения в Параметрах и меню «Пуск», экран «Давайте завершим настройку» " +
                          "и молча ставить рекламируемые приложения (игры-«плитки» и т.п.).",
            Settings = new[]
            {
                Cu(Cdm, "SubscribedContent-338389Enabled", 0, "Советы и подсказки: выкл.", def: 1),
                Cu(Cdm, "SubscribedContent-338388Enabled", 0, "Предложения в меню «Пуск»: выкл.", def: 1),
                Cu(Cdm, "SubscribedContent-310093Enabled", 0, "Экран приветствия после обновлений: выкл.", def: 1),
                Cu(Cdm, "SubscribedContent-353694Enabled", 0, "Предложения в Параметрах: выкл.", def: 1),
                Cu(Cdm, "SubscribedContent-353696Enabled", 0, "Предложения в Параметрах (2): выкл.", def: 1),
                Cu(Cdm, "SystemPaneSuggestionsEnabled", 0, "Предложения в списке приложений: выкл.", def: 1),
                Cu(Cdm, "SilentInstalledAppsEnabled", 0, "Автоустановка рекламных приложений: выкл.", def: 1),
                Cu(Cdm, "SoftLandingEnabled", 0, "Всплывающие подсказки: выкл.", def: 1),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, "«Давайте завершим настройку»: выкл.", def: 1),
            },
        },
        new RegistryTweak
        {
            TweakId = "priv.startrecs",
            Name = "Убрать рекомендации из меню «Пуск»",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            NotApplicableWhen = OnlyWindows11,
            Games = "Не влияет на FPS",
            Description = "Меню «Пуск» Windows 11 не показывает рекомендованные приложения и сайты. Закреплённые приложения на месте.",
            Settings = new[] { Cu(Advanced, "Start_IrisRecommendations", 0, "Рекомендации в «Пуске»: выкл.", def: 1) },
        },
        new RegistryTweak
        {
            TweakId = "priv.feedback",
            Name = "Не спрашивать отзывы и не собирать историю действий",
            Group = TweakCategory.Privacy,
            Effect = Effectiveness.Low,
            Games = "Не влияет на FPS",
            Description = "Windows перестаёт показывать опросы «Оцените Windows» и не отправляет журнал действий (какие файлы и приложения открывались).",
            Settings = new[]
            {
                Cu(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, "Запросы отзывов: никогда"),
                Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, "Журнал действий: не отправлять (политика)"),
            },
        },
        new ServiceTweak
        {
            TweakId = "svc.telemetry",
            Name = "Телеметрия: службы в «Вручную», минимальный уровень данных",
            Effect = Effectiveness.Low,
            IsRecommended = false,
            Games = "Эффект на FPS минимальный: служба изредка нагружает диск и сеть, чаще после обновлений",
            Description = "«Функциональные возможности для подключённых пользователей и телеметрия» (DiagTrack) и служба WAP-сообщений переводятся в «Вручную» и останавливаются; " +
                          "уровень диагностики — «Обязательные данные» (минимум, разрешённый Windows). Службы не удаляются. Обновления Windows и драйверов работают как раньше.",
            Services = new[]
            {
                new ServiceSetting("DiagTrack", ServiceStart.Manual, Stop: true, "DiagTrack → вручную"),
                new ServiceSetting("dmwappushservice", ServiceStart.Manual, Stop: true, "dmwappushservice → вручную"),
            },
            Registry = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1, "Диагностические данные: только обязательные (политика)") },
        },
        new ServiceTweak
        {
            TweakId = "svc.wsearch",
            Name = "Индексирование поиска Windows в «Вручную»",
            Effect = Effectiveness.HardwareDependent,
            Level = TweakRisk.Medium,
            RiskText = "Средний: поиск по содержимому файлов и в Outlook станет медленнее. Откат возвращает службу",
            Games = "Заметно на HDD: индексатор перестаёт читать диск во время игры. На SSD эффекта почти нет",
            Description = "Служба Windows Search постоянно индексирует файлы. На медленном жёстком диске это даёт фризы при подгрузке игры. " +
                          "Поиск программ в «Пуске» продолжит работать, поиск по тексту внутри документов — медленнее.",
            NotApplicableWhen = ctx => ctx.Snapshot.Disks.Count > 0 && ctx.Snapshot.Disks.All(d => d.Kind is DiskKind.Ssd or DiskKind.Nvme)
                ? "Все диски — SSD: индексатор почти не мешает, пункт не нужен."
                : null,
            Services = new[] { new ServiceSetting("WSearch", ServiceStart.Manual, Stop: true, "WSearch → вручную") },
        },
        new ServiceTweak
        {
            TweakId = "svc.spooler",
            Name = "Диспетчер печати в «Вручную» (если нет принтера)",
            Effect = Effectiveness.Low,
            Games = "Эффект минимальный: экономит немного памяти и закрывает лишний сетевой сервис",
            Description = "Если принтера нет, служба печати только занимает память. Показывается, только когда реальные принтеры не найдены. " +
                          "Подключите принтер позже — Windows сама запустит службу, или верните кнопкой «Откат».",
            NotApplicableWhen = ctx => Printers.HasRealPrinter(ctx.Registry) ? "Найден принтер — служба печати нужна." : null,
            Services = new[] { new ServiceSetting("Spooler", ServiceStart.Manual, Stop: true, "Spooler → вручную") },
        },
        new ServiceTweak
        {
            TweakId = "svc.maps",
            Name = "Диспетчер скачанных карт в «Вручную»",
            Effect = Effectiveness.Low,
            Games = "Эффект минимальный",
            Description = "Служба приложения «Карты» проверяет обновления офлайн-карт. Если вы не пользуетесь «Картами» Windows, она не нужна в автозапуске.",
            Services = new[] { new ServiceSetting("MapsBroker", ServiceStart.Manual, Stop: true, "MapsBroker → вручную") },
        },

        // ---------- A3: taskbar, Start and Explorer ----------
        new RegistryTweak
        {
            TweakId = "ui.taskbar",
            Name = "Почистить панель задач",
            Group = TweakCategory.Interface,
            Effect = Effectiveness.Low,
            Games = "Не влияет на FPS",
            Description = "Скрывает кнопку «Представление задач» и «Чат», поиск сворачивается в значок. Ничего не удаляется — вернуть можно в Параметрах или откатом.",
            Settings = new[]
            {
                Cu(Advanced, "ShowTaskViewButton", 0, "Кнопка «Представление задач»: скрыть", def: 1),
                Cu(Advanced, "TaskbarMn", 0, "Кнопка «Чат»: скрыть", def: 1),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1, "Поиск на панели задач: значок", def: 2),
            },
        },
        new RegistryTweak
        {
            TweakId = "ui.explorer",
            Name = "Проводник: расширения файлов, «Этот компьютер», без недавних файлов",
            Group = TweakCategory.Interface,
            Effect = Effectiveness.Low,
            IsRecommended = true,
            Games = "Не влияет на FPS",
            Description = "Показывает расширения файлов (видно, что «фото.jpg.exe» — программа), Проводник открывается на «Этот компьютер», " +
                          "в панели быстрого доступа не копятся недавние файлы и папки, нет рекламы OneDrive и других сервисов.",
            Settings = new[]
            {
                Cu(Advanced, "HideFileExt", 0, "Показывать расширения файлов", def: 1),
                Cu(Advanced, "LaunchTo", 1, "Проводник открывается на «Этот компьютер»", def: 2),
                Cu(Advanced, "ShowSyncProviderNotifications", 0, "Реклама сервисов в Проводнике: выкл.", def: 1),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, "Недавние файлы в быстром доступе: выкл.", def: 1),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0, "Часто используемые папки: выкл.", def: 1),
            },
        },
    };
}

/// <summary>Installed printers, ignoring the virtual ones Windows adds by itself.</summary>
public static class Printers
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

    private static readonly string[] Virtual = { "Microsoft Print to PDF", "Microsoft XPS Document Writer", "Fax", "OneNote", "Send To OneNote", "Факс" };

    public static bool HasRealPrinter(IRegistry reg) =>
        reg.GetSubKeyNames(RegHive.LocalMachine, Key).Any(p => !Virtual.Any(v => p.StartsWith(v, StringComparison.OrdinalIgnoreCase)));
}
