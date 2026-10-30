using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

public sealed class PowerPlanTweak : Tweak
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public override string Id => "win.powerplan";
    public override string Title => "План питания «Высокая производительность»";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;

    public override string Explanation =>
        "Не даёт процессору сбрасывать частоту и «засыпать» ядрам во время игры. Заметнее всего на ноутбуках и при просадках в драках; " +
        "на мощных настольных ПК разница часто в пределах погрешности. Увеличивает энергопотребление — после игры можно откатить.";

    public override string? ManualSteps =>
        "Если плана нет (ноутбуки с Modern Standby): Параметры → Система → Питание и батарея → Режим питания → «Максимальная производительность». На ноутбуке играйте от сети.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var plan = ctx.Snapshot.PowerPlan;
        if (plan is null)
        {
            return Result(TweakStatus.ManualOnly, "Не удалось прочитать план питания.");
        }

        if (plan.ActiveGuid == HighPerformance || plan.ActiveGuid == UltimatePerformance)
        {
            return Result(TweakStatus.AlreadyOptimal, $"Активен «{plan.ActiveName}».");
        }

        var recommendedHere = ctx.Snapshot.IsLaptop || ctx.Snapshot.Profile != PcProfile.Strong;
        if (plan.Available.All(p => p.Guid != HighPerformance))
        {
            return Result(TweakStatus.ManualOnly, $"Активен «{plan.ActiveName}», плана «Высокая производительность» в системе нет. {ManualSteps}", recommendedHere: recommendedHere);
        }

        var action = PlannedAction.Run("powercfg", $"/setactive {HighPerformance}", "powercfg", $"/setactive {plan.ActiveGuid}",
            $"План питания: «{plan.ActiveName}» → «Высокая производительность»", requiresAdmin: false);
        return Result(TweakStatus.CanApply, $"Сейчас: «{plan.ActiveName}».", new[] { action }, recommendedHere);
    }
}

public sealed class GameModeTweak : Tweak
{
    private const string Key = @"Software\Microsoft\GameBar";
    private const string Name = "AutoGameModeEnabled";

    public override string Id => "win.gamemode";
    public override string Title => "Игровой режим Windows";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;

    public override string Explanation =>
        "Windows отдаёт игре приоритет и откладывает установку обновлений и уведомления во время игры. Прирост FPS небольшой, но режим безвреден и включён по умолчанию.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var v = ctx.Registry.GetValue(RegHive.CurrentUser, Key, Name)?.AsInt();
        if (v is null or 1)
        {
            return Result(TweakStatus.AlreadyOptimal, v is null ? "Включён (значение по умолчанию)." : "Включён.");
        }

        return Result(TweakStatus.CanApply, "Выключен.", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, Key, Name, RegValue.Dword(1), "Включить игровой режим"),
        });
    }
}

public sealed class GameDvrTweak : Tweak
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string Name = "HistoricalCaptureEnabled";

    public override string Id => "win.gamedvr";
    public override string Title => "Отключить фоновую запись Xbox Game Bar";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Medium;

    public override string Explanation =>
        "Функция «Записать последние моменты» постоянно кодирует видео игры в фоне и отнимает ресурсы видеокарты и диска. " +
        "Скриншоты и ручная запись Game Bar продолжат работать.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var v = ctx.Registry.GetValue(RegHive.CurrentUser, Key, Name)?.AsInt();
        if (v is null or 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Фоновая запись выключена.");
        }

        return Result(TweakStatus.CanApply, "Фоновая запись включена — тратит ресурсы во время игры.", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, Key, Name, RegValue.Dword(0), "Выключить фоновую запись Game Bar"),
        });
    }
}

public sealed class GpuPreferenceTweak : GameTweak
{
    public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public GpuPreferenceTweak(GameDefinition game)
        : base(game)
    {
    }

    public override string Id => Target.ExeTweakId("win.gpupreference");
    public override string Title => $"{Target.ShortName} на мощной видеокарте";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.High;

    public override string Explanation =>
        "На ноутбуках с двумя видеокартами Windows может запустить игру на встроенной графике — FPS падает в разы. " +
        $"Это та же настройка, что в «Параметры → Дисплей → Графика»: для {Target.ExeFileName} выбирается «Высокая производительность».";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var steam = SteamOf(ctx);
        var exe = steam.GameExe;
        if (exe is null)
        {
            return Result(TweakStatus.NotApplicable, $"{Target.Name} не найдена");
        }

        if (ctx.Snapshot.RealGpus.Count() < 2)
        {
            return Result(TweakStatus.NotApplicable, "В системе одна видеокарта — настройка не нужна.", recommendedHere: false);
        }

        var current = ctx.Registry.GetValue(RegHive.CurrentUser, Key, exe)?.Data;
        if (RegistryStrings.GetSemicolonPair(current, "GpuPreference") == "2")
        {
            return Result(TweakStatus.AlreadyOptimal, $"Для {Target.ExeFileName} уже выбрана «Высокая производительность».");
        }

        var value = RegistryStrings.SetSemicolonPair(current, "GpuPreference", "2");
        var runtime = steam.RuntimeGpu is null ? string.Empty : $" Сейчас игра запущена на: {steam.RuntimeGpu}.";
        return Result(TweakStatus.CanApply, $"Несколько видеокарт, предпочтение для игры не задано.{runtime}", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, Key, exe, RegValue.Str(value), $"Предпочтение GPU для {Target.ExeFileName}: высокая производительность"),
        }, recommendedHere: ctx.Snapshot.IsHybridGraphics);
    }
}

public sealed class FullscreenOptimizationsTweak : GameTweak
{
    private const string Key = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string Flag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    public FullscreenOptimizationsTweak(GameDefinition game)
        : base(game)
    {
    }

    public override string Id => Target.ExeTweakId("win.fso");
    public override string Title => $"Отключить оптимизацию во весь экран для {Target.ExeFileName}";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;

    public override string Explanation =>
        $"То же, что галочка в «Свойства {Target.ExeFileName} → Совместимость». На части систем убирает микрофризы и задержку в полноэкранном режиме, " +
        "на других ничего не меняет или ухудшает работу оверлеев и HDR. Включайте, только если замер показал выигрыш.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var exe = SteamOf(ctx).GameExe;
        if (exe is null)
        {
            return Result(TweakStatus.NotApplicable, $"{Target.Name} не найдена");
        }

        var current = ctx.Registry.GetValue(RegHive.CurrentUser, Key, exe)?.Data;
        if (RegistryStrings.HasLayerFlag(current, Flag))
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже отключена.");
        }

        return Result(TweakStatus.CanApply, "Не отключена (по умолчанию Windows).", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, Key, exe, RegValue.Str(RegistryStrings.AddLayerFlag(current, Flag)), $"Отключить оптимизацию во весь экран для {Target.ExeFileName}"),
        });
    }
}

public sealed class HagsTweak : Tweak
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Name = "HwSchMode";

    public override string Id => "win.hags";
    public override string Title => "Аппаратное ускорение планирования GPU (HAGS)";
    public override TweakCategory Category => TweakCategory.Gpu;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override TweakRisk RiskLevel => TweakRisk.Medium;
    public override bool RequiresRestart => true;
    public override string Risk => "Низкий; нужна перезагрузка. Откат тоже требует перезагрузки";

    public override string Explanation =>
        "Видеокарта сама управляет своей памятью и очередью задач. Независимые тесты в играх показывают разницу около нуля; " +
        "HAGS обязателен для генерации кадров DLSS 3/4, которой нет ни в Dota 2, ни в CS2. Если после обновления драйвера появились фризы — попробуйте переключить. " +
        "Приложение не меняет HAGS автоматически: только по вашему выбору.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var gpu = ctx.Snapshot.PrimaryGpu;
        var v = ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt();
        var state = v switch
        {
            2 => "включён",
            1 => "выключен",
            _ => "по умолчанию (зависит от драйвера)",
        };
        var supported = gpu is not null && gpu.Vendor switch
        {
            GpuVendor.Nvidia => !gpu.IsIntegrated && !gpu.Name.Contains("GTX 9", StringComparison.OrdinalIgnoreCase),
            GpuVendor.Amd => !gpu.IsIntegrated,
            GpuVendor.Intel => gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        if (!supported)
        {
            return Result(TweakStatus.NotApplicable, $"Сейчас: {state}. Для этой видеокарты HAGS обычно не поддерживается.", recommendedHere: false);
        }

        var target = v == 2 ? 1 : 2;
        var action = PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(target),
            target == 2 ? "Включить HAGS (после перезагрузки)" : "Выключить HAGS (после перезагрузки)");
        return Result(TweakStatus.CanApply, $"Сейчас: {state}. Переключение — только вручную, затем перезагрузка.", new[] { action }, recommendedHere: false);
    }
}

public sealed class CpuPriorityTweak : GameTweak
{
    private const string Name = "CpuPriorityClass";

    public CpuPriorityTweak(GameDefinition game)
        : base(game)
    {
    }

    private string Key => $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{Target.ExeFileName}\PerfOptions";

    public override string Id => Target.ExeTweakId("win.priority");
    public override string Title => $"Высокий приоритет процесса {Target.ExeFileName} (через Windows)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override TweakRisk RiskLevel => TweakRisk.Medium;
    public override string Risk => "Низкий, но при нехватке ядер может «душить» звук, Discord и стрим; нужны права администратора";

    public override string Explanation =>
        $"Штатная настройка Windows (Image File Execution Options → PerfOptions): система сама запускает {Target.ExeFileName} с высоким приоритетом, без вмешательства в процесс. " +
        "Аналог параметра -high. Надёжных тестов с приростом нет — оставлено как опция для проверки замером.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (!SteamOf(ctx).GameFound)
        {
            return Result(TweakStatus.NotApplicable, $"{Target.Name} не найдена");
        }

        var v = ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt();
        if (v == 3)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже задан высокий приоритет.");
        }

        return Result(TweakStatus.CanApply, "Приоритет по умолчанию (обычный).", new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(3), $"Высокий приоритет для {Target.ExeFileName}"),
        });
    }
}

public sealed class WindowedGameOptimizationsTweak : GameTweak
{
    private const string Name = "DirectXUserGlobalSettings";
    private const string Pair = "SwapEffectUpgradeEnable";

    public WindowedGameOptimizationsTweak(GameDefinition game)
        : base(game)
    {
    }

    // One Windows-wide setting, so the id is shared by all games.
    public override string Id => "win.windowedopt";
    public override string Title => "«Оптимизация для оконных игр» Windows 11";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;

    public override string Explanation =>
        "Настройка Windows 11 (Параметры → Система → Дисплей → Графика) для DirectX 11 игр в окне и «окне без рамки»: переводит их на современный режим вывода — меньше задержка, работает VRR. " +
        "При этом пользователи Windows 11 24H2 сообщали, что именно её выключение убирало случайные фризы. Данные противоречивы, поэтому переключение — только по вашему решению и с замером. " +
        $"Влияет, только если {Target.Name} запущена не в эксклюзивном полноэкранном режиме.";

    public override string? ManualSteps => "То же вручную: Параметры → Система → Дисплей → Графика → «Изменить параметры графики по умолчанию» → «Оптимизация для оконных игр».";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (ctx.Snapshot.Windows is not { IsWindows11: true })
        {
            return Result(TweakStatus.NotApplicable, "Есть только в Windows 11.");
        }

        var current = ctx.Registry.GetValue(RegHive.CurrentUser, GpuPreferenceTweak.Key, Name)?.Data;
        var on = RegistryStrings.GetSemicolonPair(current, Pair) == "1";
        var windowed = ExclusiveFullscreenTweak.IsWindowed(ctx, Target);
        var note = windowed == false ? $" {Target.Name} сейчас в эксклюзивном полноэкранном режиме — на неё эта настройка не влияет." : string.Empty;
        var target = on ? "0" : "1";
        var action = PlannedAction.SetReg(RegHive.CurrentUser, GpuPreferenceTweak.Key, Name,
            RegValue.Str(RegistryStrings.SetSemicolonPair(current, Pair, target)),
            on ? "Выключить «Оптимизацию для оконных игр» (если есть фризы)" : "Включить «Оптимизацию для оконных игр» (меньше задержка в окне)");
        return Result(TweakStatus.CanApply, (on ? "Сейчас включена." : "Сейчас выключена (или по умолчанию Windows).") + note,
            new[] { action }, recommendedHere: false);
    }
}
