using System.Globalization;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>
/// The monitor runs below the refresh rate it supports (a 165 Hz monitor left at 60 Hz is the most common case).
/// This is the largest single win for felt latency, and it needs no tweak at all — only the right setting in Windows.
/// The app never changes the display mode itself: a wrong mode can leave a black screen.
/// </summary>
public sealed class RefreshRateTweak : Tweak
{
    public override string Id => "win.refreshrate";
    public override string Title => "Частота обновления монитора на максимум";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.High;

    public override string Explanation =>
        "Каждый кадр на мониторе 60 Гц показывается до 16,7 мс, на 144 Гц — до 6,9 мс, на 240 Гц — до 4,2 мс. Если монитор на 144/165/240 Гц остался на 60 Гц " +
        "(так бывает после смены кабеля, драйвера или сброса настроек), игра ощущается «ватной», даже при высоком FPS. Это самое заметное улучшение задержки из всех.";

    public override string? ManualSteps =>
        "Параметры → Система → Дисплей → Расширенные параметры дисплея → «Выберите частоту обновления». " +
        "Если нужной частоты нет в списке: подключите монитор кабелем DisplayPort (или HDMI нужной версии), проверьте в меню самого монитора, что высокая частота включена, и обновите драйвер видеокарты.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var d = ctx.Snapshot.Display;
        if (d is null || d.RefreshHz <= 0 || d.MaxRefreshHz <= 0)
        {
            return Result(TweakStatus.NotApplicable, "Не удалось определить частоту монитора.", recommendedHere: false);
        }

        if (d.BelowMaxRefresh)
        {
            return Result(TweakStatus.ManualOnly,
                $"Монитор работает на {d.RefreshHz} Гц, а при разрешении {d.Width}×{d.Height} поддерживает до {d.MaxRefreshHz} Гц. {ManualSteps}");
        }

        return Result(TweakStatus.AlreadyOptimal, $"Монитор уже работает на максимальной частоте: {d.RefreshHz} Гц.", recommendedHere: false);
    }
}

/// <summary>
/// NVIDIA Reflex / AMD Anti-Lag 2 are switches inside the game itself. The app tells which one applies to this video card
/// and where it is, but does not touch the game's video config for it: the key is not documented by the developer.
/// </summary>
public sealed class LowLatencyModeTweak : GameTweak
{
    public LowLatencyModeTweak(GameDefinition game)
        : base(game)
    {
    }

    public override string Id => Target.TweakId("lowlatency");
    public override string Title => $"Режим низкой задержки в {Target.ShortName} (Reflex / Anti-Lag)";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Effectiveness.Medium;

    public override string Explanation =>
        "Функция видеокарты, встроенная в игру: игра и драйвер синхронизируют очередь кадров, и между нажатием кнопки и картинкой проходит меньше времени. " +
        "FPS она не добавляет — она убирает «хвост» очереди, заметный, когда видеокарта загружена почти полностью. Включается в настройках игры, а не в Windows.";

    public override string Risk => "Нет: это настройка в самой игре";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var gpu = ctx.Snapshot.PrimaryGpu;
        switch (gpu?.Vendor)
        {
            case GpuVendor.Nvidia:
                return Result(TweakStatus.ManualOnly, Target.ReflexHint);
            case GpuVendor.Amd:
                return Result(TweakStatus.ManualOnly, Target.AmdAntiLagHint);
            default:
                return Result(TweakStatus.NotApplicable,
                    $"У этой видеокарты нет встроенной в {Target.ShortName} функции низкой задержки. Помогают V-Sync «выкл», эксклюзивный полноэкранный режим и ограничение FPS.",
                    recommendedHere: false);
        }
    }
}

/// <summary>
/// Windows 10 (2004) and Windows 11 make a program's high-resolution timer request (many games ask for 1 ms) apply only to that program,
/// and Windows 11 ignores it when the window is hidden. This value restores the old, system-wide behaviour. It exists only in
/// Windows 11 and Windows Server 2022 (build 20348) and newer; it does not set any resolution by itself.
/// </summary>
public sealed class GlobalTimerResolutionTweak : Tweak
{
    public const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    public const string Name = "GlobalTimerResolutionRequests";

    public override string Id => "win.timerres";
    public override string Title => "Глобальные запросы точного таймера (Windows 11)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override TweakRisk RiskLevel => TweakRisk.Medium;
    public override bool RequiresRestart => true;
    public override string Risk => "Низкий: чуть выше энергопотребление и нагрев в простое. Нужны права администратора и перезагрузка";

    public override string Explanation =>
        "Начиная с Windows 10 (2004) запрос высокой точности таймера действует только на запросившую программу, а в Windows 11 игнорируется у свёрнутых окон. " +
        "Значение возвращает старое поведение: запрос любой программы действует на всю систему. Само оно точность не задаёт — 0,5 или 1 мс включает игра или другая программа. " +
        "Работает только в Windows 11. Иногда выравнивает время кадра, иногда ничего не меняет; сторонние тесты расходятся. Включайте, только если проверили замером.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (ctx.Snapshot.Windows is { Build: < 19041 })
        {
            return Result(TweakStatus.NotApplicable, "Нужна Windows 10 версии 2004 или новее: в более ранних версиях таймер и так общий.", recommendedHere: false);
        }

        if (ctx.Snapshot.Windows is { Build: < 20348 })
        {
            return Result(TweakStatus.NotApplicable, "Только Windows 11: в Windows 10 этого значения нет, Windows его не читает.", recommendedHere: false);
        }

        if (ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt() == 1)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже включено.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Не задано: действует стандартное поведение Windows.", new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(1), "Таймер высокого разрешения действует на всю систему (после перезагрузки)"),
        }, recommendedHere: false);
    }
}

/// <summary>
/// Multiplane overlay (MPO) lets the display hardware compose several windows itself. On some driver builds it causes flicker,
/// single black frames and stutter when switching between windows. The widely used workaround is to switch it off in the compositor settings.
/// </summary>
public sealed class MultiplaneOverlayTweak : Tweak
{
    public const string Key = @"SOFTWARE\Microsoft\Windows\Dwm";
    public const string Name = "OverlayTestMode";

    public override string Id => "win.mpo";
    public override string Title => "Отключить Multiplane Overlay (если мерцает или фризит)";
    public override TweakCategory Category => TweakCategory.Gpu;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override TweakRisk RiskLevel => TweakRisk.Medium;
    public override bool RequiresRestart => true;
    public override string Risk => "Низкий, откатывается. Нужны права администратора и перезагрузка";

    public override string Explanation =>
        "Помогает, если в окне или «окне без рамки» бывают мерцание, одиночные чёрные кадры или рывки при переключении между окнами — проблема некоторых версий драйверов NVIDIA и AMD. " +
        "Если таких симптомов нет, выигрыша не будет, поэтому по умолчанию пункт выключен. Откат удаляет значение и возвращает стандартное поведение.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (ctx.Registry.GetValue(RegHive.LocalMachine, Key, Name)?.AsInt() == 5)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже отключён.", recommendedHere: false);
        }

        // Windows 11 24H2 (build 26100) is reported to ignore the value on part of the systems; Microsoft doesn't document it.
        var note = ctx.Snapshot.Windows is { Build: >= 26100 } ? " В Windows 11 24H2 и новее, по отчётам пользователей, Windows может его не учитывать." : string.Empty;
        return Result(TweakStatus.CanApply, "Включён (по умолчанию Windows)." + note, new[]
        {
            PlannedAction.SetReg(RegHive.LocalMachine, Key, Name, RegValue.Dword(5), "Отключить Multiplane Overlay (после перезагрузки)"),
        }, recommendedHere: false);
    }
}

/// <summary>
/// "USB selective suspend" lets Windows power down a USB port that looks idle. A mouse or keyboard behind a receiver or a hub can then wake up
/// with a tiny delay or lose the first movement. Only the active power plan is changed, and the old values are restored on undo.
/// </summary>
public sealed class UsbSelectiveSuspendTweak : Tweak
{
    public const string UsbSubgroup = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string SelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

    public override string Id => "win.usbsuspend";
    public override string Title => "Не усыплять USB-порты (мышь и клавиатура)";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override string Risk => "Минимальный, откатывается; на ноутбуке от батареи расход чуть выше. Нужны права администратора";

    public override string Explanation =>
        "Windows отключает питание USB-порта, который кажется неактивным, и выводит его из сна при первом движении — это может дать задержку первого движения мыши или клавиши. " +
        "Заметно у беспроводных приёмников и через USB-хабы; у проводной мыши в порту компьютера эффекта обычно нет. Меняется только активный план питания.";

    private static string SettingKey(Guid scheme) =>
        $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{scheme}\{UsbSubgroup}\{SelectiveSuspend}";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var plan = ctx.Snapshot.PowerPlan;
        if (plan is null)
        {
            return Result(TweakStatus.ManualOnly, "Не удалось прочитать план питания. Дополнительные параметры питания → Параметры USB → «Параметр временного отключения USB-порта» → «Запрещено».", recommendedHere: false);
        }

        // The value is stored only when it differs from the plan's default, and the default is "enabled" (1).
        var key = SettingKey(plan.ActiveGuid);
        var ac = ctx.Registry.GetValue(RegHive.LocalMachine, key, "ACSettingIndex")?.AsInt() ?? 1;
        var dc = ctx.Registry.GetValue(RegHive.LocalMachine, key, "DCSettingIndex")?.AsInt() ?? 1;
        if (ac == 0 && dc == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, $"В плане «{plan.ActiveName}» усыпление USB уже выключено.", recommendedHere: false);
        }

        var scheme = plan.ActiveGuid.ToString();
        var actions = new List<PlannedAction>
        {
            // setactive both before and after: undo runs backwards, so the last step of an undo re-applies the restored plan.
            PlannedAction.Run("powercfg", $"/setactive {scheme}", "powercfg", $"/setactive {scheme}", "Активировать текущий план питания", requiresAdmin: true),
        };
        if (ac != 0)
        {
            actions.Add(PlannedAction.Run("powercfg", $"/setacvalueindex {scheme} {UsbSubgroup} {SelectiveSuspend} 0",
                "powercfg", $"/setacvalueindex {scheme} {UsbSubgroup} {SelectiveSuspend} {ac.ToString(CultureInfo.InvariantCulture)}",
                "Усыпление USB от сети: запрещено", requiresAdmin: true));
        }

        if (dc != 0)
        {
            actions.Add(PlannedAction.Run("powercfg", $"/setdcvalueindex {scheme} {UsbSubgroup} {SelectiveSuspend} 0",
                "powercfg", $"/setdcvalueindex {scheme} {UsbSubgroup} {SelectiveSuspend} {dc.ToString(CultureInfo.InvariantCulture)}",
                "Усыпление USB от батареи: запрещено", requiresAdmin: true));
        }

        actions.Add(PlannedAction.Run("powercfg", $"/setactive {scheme}", "powercfg", $"/setactive {scheme}", "Применить изменения плана питания", requiresAdmin: true));
        return Result(TweakStatus.CanApply, $"В плане «{plan.ActiveName}» усыпление USB включено (по умолчанию Windows).", actions, recommendedHere: false);
    }
}
