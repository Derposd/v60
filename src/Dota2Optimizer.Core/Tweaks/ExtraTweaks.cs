using System.Globalization;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>
/// Shift ×5, holding the right Shift and holding Num Lock open accessibility prompts that minimise the game.
/// Only the shortcuts are switched off; the features themselves stay available in Settings.
/// </summary>
public sealed class AccessibilityHotkeysTweak : Tweak
{
    /// <summary>Bit 0x1 = the feature is on, bit 0x4 = its keyboard shortcut is active (same values as SKF_/FKF_/TKF_ flags).</summary>
    public const int FeatureOn = 0x1;
    public const int HotkeyActive = 0x4;

    public static readonly (string Key, string Name, int Default)[] Features =
    {
        (@"Control Panel\Accessibility\StickyKeys", "залипание клавиш (Shift ×5)", 510),
        (@"Control Panel\Accessibility\Keyboard Response", "фильтрация ввода (удержание правого Shift)", 126),
        (@"Control Panel\Accessibility\ToggleKeys", "озвучивание переключения (удержание Num Lock)", 62),
    };

    public override string Id => "win.stickykeys";
    public override string Title => "Отключить всплывающее «Залипание клавиш» (Shift ×5)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;

    public override string Explanation =>
        "Не про FPS, а про то, чтобы игра не сворачивалась: пять нажатий Shift, удержание правого Shift или Num Lock открывают окно специальных возможностей поверх игры. " +
        "Выключаются только эти сочетания клавиш — сами функции остаются в «Параметры → Специальные возможности → Клавиатура».";

    public static int ReadFlags(IRegistry reg, string key, int fallback) =>
        int.TryParse(reg.GetValue(RegHive.CurrentUser, key, "Flags")?.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : fallback;

    public override TweakPlan Plan(TweakContext ctx)
    {
        var actions = new List<PlannedAction>();
        var inUse = new List<string>();
        foreach (var (key, name, fallback) in Features)
        {
            var flags = ReadFlags(ctx.Registry, key, fallback);
            if ((flags & FeatureOn) != 0)
            {
                // The user turned the feature on: they rely on it, leave its shortcut alone.
                inUse.Add(name);
                continue;
            }

            if ((flags & HotkeyActive) != 0)
            {
                actions.Add(PlannedAction.SetReg(RegHive.CurrentUser, key, "Flags",
                    RegValue.Str((flags & ~HotkeyActive).ToString(CultureInfo.InvariantCulture)), $"Выключить сочетание клавиш: {name}"));
            }
        }

        var note = inUse.Count == 0 ? string.Empty : $" Включено вами и не трогается: {string.Join(", ", inUse)}.";
        return actions.Count == 0
            ? Result(TweakStatus.AlreadyOptimal, "Сочетания клавиш уже выключены." + note)
            : Result(TweakStatus.CanApply, $"Включено сочетаний: {actions.Count}. Применяется сразу, без перезагрузки." + note, actions);
    }
}

/// <summary>"Enhance pointer precision" (Windows mouse acceleration).</summary>
public sealed class MouseAccelerationTweak : GameTweak
{
    public const string Key = @"Control Panel\Mouse";

    public MouseAccelerationTweak(GameDefinition game)
        : base(game)
    {
    }

    // One Windows-wide setting, so the id is shared by all games.
    public override string Id => "win.mouseaccel";
    public override string Title => "Отключить ускорение мыши Windows («Повышенная точность указателя»)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Target.Id == GameId.Cs2 ? Effectiveness.Low : Effectiveness.Medium;
    public override bool Recommended => false;

    public override string Explanation => Target.Id == GameId.Cs2
        ? "Не влияет на FPS. CS2 читает мышь напрямую (raw input), поэтому ускорение Windows на прицел не действует — только на меню и рабочий стол. " +
          "Включайте, если хотите одинаковое ощущение мыши везде. Применяется сразу; курсор на рабочем столе станет чуть медленнее на быстрых движениях."
        : "Не влияет на FPS, но влияет на точность: Dota 2 двигает обычный курсор Windows, и с ускорением расстояние зависит от скорости руки. " +
          "Это та же галочка, что в «Панель управления → Мышь → Параметры указателя». Применяется сразу; курсор на рабочем столе станет чуть медленнее на быстрых движениях.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var speed = ctx.Registry.GetValue(RegHive.CurrentUser, Key, "MouseSpeed")?.Data;
        if (speed == "0")
        {
            return Result(TweakStatus.AlreadyOptimal, "Ускорение мыши уже выключено.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Ускорение мыши включено (по умолчанию Windows).", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, Key, "MouseSpeed", RegValue.Str("0"), "Выключить «Повышенную точность указателя»"),
            PlannedAction.SetReg(RegHive.CurrentUser, Key, "MouseThreshold1", RegValue.Str("0"), "Порог ускорения 1 = 0"),
            PlannedAction.SetReg(RegHive.CurrentUser, Key, "MouseThreshold2", RegValue.Str("0"), "Порог ускорения 2 = 0"),
        }, recommendedHere: false);
    }
}

/// <summary>Windows power throttling (EcoQoS) for background processes.</summary>
public sealed class PowerThrottlingTweak : Tweak
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string Name = "PowerThrottlingOff";

    public override string Id => "win.powerthrottling";
    public override string Title => "Отключить Power Throttling для фоновых программ";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override string Risk => "Низкий; на ноутбуке выше нагрев и расход батареи. Нужны права администратора";

    public override string Explanation =>
        "Windows снижает частоту процессора для программ в фоне (EcoQoS), чтобы экономить энергию. Игра на переднем плане под это не попадает, " +
        "но Discord, OBS и запись — могут, и тогда звук или стрим начинают заикаться. Если такой проблемы нет, выигрыша не будет.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt() == 1)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже отключён.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Включён (по умолчанию Windows).", new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(1), "Отключить Power Throttling"),
        }, recommendedHere: false);
    }
}

/// <summary>The classic "gaming tweak" for the multimedia scheduler (MMCSS) Games task.</summary>
public sealed class MmcssGamesTweak : Tweak
{
    private const string Profile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string Games = Profile + @"\Tasks\Games";

    private static readonly (string Key, string Name, RegValue Value, string Text)[] Targets =
    {
        (Profile, "SystemResponsiveness", RegValue.Dword(10), "Резерв CPU для фоновых задач: 10%"),
        (Games, "GPU Priority", RegValue.Dword(8), "Приоритет GPU для задач «Games»: 8"),
        (Games, "Priority", RegValue.Dword(6), "Приоритет задач «Games»: 6"),
        (Games, "Scheduling Category", RegValue.Str("High"), "Категория планирования «Games»: High"),
        (Games, "SFIO Priority", RegValue.Str("High"), "Приоритет ввода-вывода «Games»: High"),
    };

    public override string Id => "win.mmcss";
    public override string Title => "Приоритет игр в планировщике мультимедиа (MMCSS)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override string Risk => "Низкий, полностью откатывается. Нужны права администратора";

    public override string Explanation =>
        "Классическая настройка из сборников «игровых твиков»: служба MMCSS отдаёт приоритет задачам категории «Games». " +
        "Только игры, которые сами регистрируются в MMCSS, получают выигрыш; Source 2 этого не делает, поэтому в Dota 2 и CS2 эффект обычно нулевой. Оставлено для проверки замером.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var actions = Targets
            .Where(t => !Same(ctx.Registry.GetValue(RegHive.LocalMachine, t.Key, t.Name), t.Value))
            .Select(t => PlannedAction.SetReg(RegHive.LocalMachine, t.Key, t.Name, t.Value, t.Text))
            .ToList();
        return actions.Count == 0
            ? Result(TweakStatus.AlreadyOptimal, "Уже настроено.", recommendedHere: false)
            : Result(TweakStatus.CanApply, "Значения Windows по умолчанию.", actions, recommendedHere: false);
    }

    private static bool Same(RegValue? current, RegValue wanted) =>
        current is not null && (wanted.Kind == RegKind.DWord
            ? current.AsInt()?.ToString(CultureInfo.InvariantCulture) == wanted.Data
            : string.Equals(current.Data, wanted.Data, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Delivery Optimization uploading Windows updates to other PCs over the internet.</summary>
public sealed class DeliveryOptimizationTweak : Tweak
{
    private const string Key = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string Name = "DODownloadMode";

    public override string Id => "win.deliveryopt";
    public override string Title => "Не раздавать обновления Windows другим компьютерам";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override string Risk => "Минимальный, откатывается. Нужны права администратора";

    public override string Explanation =>
        "Оптимизация доставки может отдавать скачанные обновления другим компьютерам и занимать исходящий канал во время игры. " +
        "Обновления Windows продолжат скачиваться как обычно — только напрямую с серверов Microsoft. " +
        "В Параметрах появится надпись «Некоторыми параметрами управляет ваша организация» — это из-за этой настройки, откат её убирает.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var v = ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt();
        if (v is 0 or 99 or 100)
        {
            return Result(TweakStatus.AlreadyOptimal, "Раздача обновлений уже выключена.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Windows может раздавать обновления другим ПК.", new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(0), "Скачивать обновления только с серверов Microsoft"),
        }, recommendedHere: false);
    }
}

/// <summary>
/// Memory integrity (HVCI). Off only by the user's explicit choice: the tweak is never recommended or preselected and asks for consent
/// (<see cref="Tweak.RequiresConsent"/>) before it is applied. When a UEFI lock or a group policy keeps it on, Windows ignores the
/// registry value, so the tweak says so instead of writing it.
/// </summary>
public sealed class MemoryIntegrityTweak : Tweak
{
    public const string Key = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    public const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";

    public override string Id => "win.memoryintegrity";
    public override string Title => "Отключить целостность памяти (HVCI)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override bool RequiresRestart => true;
    public override TweakRisk RiskLevel => TweakRisk.High;
    public override string Risk => "Снижает защиту ядра Windows от вредоносных драйверов. Нужны права администратора и перезагрузка";

    public override string Explanation =>
        "Защита Windows от вредоносных и уязвимых драйверов. В тестах она стоит от 0 до 5–10% FPS там, где игра упирается в процессор. " +
        "Microsoft допускает её отключение на время игры с возвратом после. Откат включает защиту обратно (тоже после перезагрузки).";

    public override string? ManualSteps =>
        "Безопасность Windows → Безопасность устройства → Сведения об изоляции ядра → «Целостность памяти». Нужна перезагрузка.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var enabled = ctx.Registry.GetValue(RegHive.LocalMachine, Key, "Enabled")?.AsInt();
        if (enabled != 1)
        {
            return Result(TweakStatus.AlreadyOptimal, enabled is null ? "Не включена на этом ПК." : "Выключена.", recommendedHere: false);
        }

        if (ctx.Registry.GetValue(RegHive.LocalMachine, Key, "Locked")?.AsInt() == 1)
        {
            return Result(TweakStatus.ManualOnly, "Включена с блокировкой UEFI: через реестр её не выключить. " + ManualSteps, recommendedHere: false);
        }

        if (ctx.Registry.GetValue(RegHive.LocalMachine, PolicyKey, "HypervisorEnforcedCodeIntegrity")?.AsInt() is 1 or 2)
        {
            return Result(TweakStatus.ManualOnly, "Включена групповой политикой (например, на рабочем ПК): программа её не меняет.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Включена. Выключится после перезагрузки.", new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, "Enabled", RegValue.Dword(0), "Целостность памяти: выкл. (после перезагрузки)"),
        }, recommendedHere: false);
    }
}
