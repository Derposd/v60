using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Diagnostics;

/// <summary>
/// "Why does the mouse feel late": the causes that really add input delay, checked against this PC and the game's own video config.
/// Nothing here is a tweak; it only explains what to look at, in order of effect.
/// </summary>
public static class InputLagAdvisor
{
    private const string VSyncKey = "setting.mat_vsync";

    /// <summary>V-Sync as the game's video config says: true = on, false = off, null = unknown (no config or no such key).</summary>
    public static bool? ReadVSync(IFileSystem fs, SteamInfo steam)
    {
        foreach (var path in steam.VideoConfigPaths)
        {
            try
            {
                if (fs.FileExists(path) && VideoConfig.Parse(fs.ReadAllText(path)).Get(VSyncKey) is { } v)
                {
                    return v.Trim() != "0";
                }
            }
            catch (IOException)
            {
                // An unreadable config only means "unknown".
            }
        }

        return null;
    }

    public static IReadOnlyList<Advice> Evaluate(GameDefinition game, SystemSnapshot s, IReadOnlyList<TweakPlan> plans, bool? vSyncOn)
    {
        var list = new List<Advice>();

        if (s.Display is { BelowMaxRefresh: true } d)
        {
            list.Add(new Advice(Severity.Warning, $"Задержка ввода: монитор работает на {d.RefreshHz} Гц, а поддерживает {d.MaxRefreshHz} Гц",
                "Это самая частая причина «ватной» мыши. Выберите максимальную частоту: Параметры → Система → Дисплей → Расширенные параметры дисплея. " +
                "Если её нет в списке — проверьте кабель (лучше DisplayPort) и режим в меню монитора."));
        }

        if (vSyncOn == true)
        {
            list.Add(new Advice(Severity.Warning, $"Задержка ввода: в {game.ShortName} включён V-Sync",
                "V-Sync держит готовые кадры в очереди до обновления монитора и добавляет заметную задержку. Любой графический пресет программы выключает его; " +
                "на мониторе с G-Sync/FreeSync включите синхронизацию в панели драйвера, а в игре оставьте выключенной и ограничьте FPS на 3 ниже частоты монитора."));
        }

        var fullscreen = plans.FirstOrDefault(p => p.Tweak.Id == game.TweakId("fullscreen"));
        if (fullscreen is { Status: TweakStatus.CanApply })
        {
            list.Add(new Advice(Severity.Info, $"Задержка ввода: {game.ShortName} работает в окне или «окне без рамки»",
                "В эксклюзивном полноэкранном режиме игра сама выводит кадр на монитор без прослойки рабочего стола — задержка обычно ниже. Минус: Alt+Tab медленнее. Включается в плане, пункт «Эксклюзивный полноэкранный режим»."));
        }

        var lowLatency = plans.FirstOrDefault(p => p.Tweak.Id == game.TweakId("lowlatency"));
        if (lowLatency is { Status: TweakStatus.ManualOnly })
        {
            list.Add(new Advice(Severity.Info, $"Задержка ввода: проверьте режим низкой задержки в {game.ShortName}", lowLatency.StatusText));
        }

        var power = plans.FirstOrDefault(p => p.Tweak.Id == "win.powerplan");
        if (power is { Status: TweakStatus.CanApply } && (s.IsLaptop || s.Profile != PcProfile.Strong))
        {
            list.Add(new Advice(Severity.Info, "Задержка ввода: процессор снижает частоту в лёгких сценах",
                "При плане питания «Сбалансированная» частота растёт не мгновенно, и первые кадры после паузы выходят позже. План «Высокая производительность» убирает этот эффект."));
        }

        return list;
    }
}
