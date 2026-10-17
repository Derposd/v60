using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Diagnostics;

/// <summary>Step 2 of the wizard: what hurts performance on this PC, most important first.</summary>
public static class DiagnosisEngine
{
    public static IReadOnlyList<Advice> Diagnose(
        SystemSnapshot s,
        IReadOnlyList<TweakPlan> plans,
        IReadOnlyList<Advice> health,
        IReadOnlyList<RunningApp> background,
        DateTime today)
    {
        var list = new List<Advice>();

        if (!s.Steam.DotaFound)
        {
            list.Add(new Advice(Severity.Critical, "Dota 2 не найдена",
                s.Steam.SteamPath is null ? "Steam не найден в реестре." : "Нет appmanifest_570.acf ни в одной библиотеке Steam. Установите игру или укажите библиотеку в Steam."));
        }

        foreach (var gpu in s.RealGpus.Where(g => !g.IsIntegrated || !s.IsHybridGraphics))
        {
            list.AddRange(DriverAdvisor.Check(gpu, s.Windows, today));
        }

        if (!s.RealGpus.Any())
        {
            list.Add(new Advice(Severity.Critical, "Не найдена видеокарта с драйвером", "Установите драйвер видеокарты с сайта производителя."));
        }

        if (s.IsHybridGraphics)
        {
            var dgpu = s.RealGpus.First(g => !g.IsIntegrated);
            if (s.DotaRuntimeGpu is not null && !string.Equals(s.DotaRuntimeGpu, dgpu.Name, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new Advice(Severity.Critical, "Dota 2 запущена на встроенной графике",
                    $"Сейчас игра рендерится на «{s.DotaRuntimeGpu}», а не на «{dgpu.Name}». Примените твик «Dota 2 на мощной видеокарте» и перезапустите игру. На ноутбуке также проверьте режим MUX/«только дискретная графика» в утилите производителя."));
            }
            else if (s.DotaRuntimeGpu is null)
            {
                list.Add(new Advice(Severity.Info, "Гибридная графика",
                    $"Встроенная + {dgpu.Name}. Чтобы увидеть, на какой карте реально идёт игра, запустите Dota 2 и нажмите «Пересканировать»."));
            }
        }

        if (s.IsLaptop)
        {
            list.Add(new Advice(Severity.Info, "Ноутбук",
                "Играйте от сети: на батарее видеокарта и процессор работают на пониженных частотах. Ограничение FPS снижает нагрев и троттлинг."));
        }

        foreach (var p in plans.Where(p => p.Status == TweakStatus.CanApply && p.RecommendedHere && !p.Tweak.Disputed))
        {
            var sev = p.Tweak.Effectiveness == Effectiveness.High ? Severity.Warning : Severity.Info;
            list.Add(new Advice(sev, $"Можно улучшить: {p.Tweak.Title}", $"{p.StatusText} Эффект: {p.Tweak.Effectiveness.Ru()}."));
        }

        foreach (var p in plans.Where(p => p.Status == TweakStatus.Blocked))
        {
            list.Add(new Advice(Severity.Warning, $"Сейчас недоступно: {p.Tweak.Title}", p.StatusText));
        }

        list.AddRange(health);

        foreach (var app in background.Where(a => a.App.Category is "Загрузки" or "Запись" or "Синхронизация" or "Лаунчер"))
        {
            list.Add(new Advice(Severity.Warning, $"Фоновая программа: {app.App.DisplayName}", app.App.Advice));
        }

        foreach (var app in background.Where(a => a.App.Category is not ("Загрузки" or "Запись" or "Синхронизация" or "Лаунчер")))
        {
            list.Add(new Advice(Severity.Info, $"Фоновая программа: {app.App.DisplayName} ({app.WorkingSetMb} МБ)", app.App.Advice));
        }

        return list
            .Select((a, i) => (a, i))
            .OrderByDescending(x => x.a.Severity)
            .ThenBy(x => x.i)
            .Select(x => x.a)
            .ToList();
    }
}
