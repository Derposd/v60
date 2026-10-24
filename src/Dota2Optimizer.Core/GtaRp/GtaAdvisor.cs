using System.Globalization;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.GtaRp;

/// <summary>"What holds GTA V roleplay back on this PC", most important first. Reads only; never changes anything.</summary>
public static class GtaAdvisor
{
    public static IReadOnlyList<Advice> Evaluate(
        GtaInfo gta,
        SystemSnapshot s,
        IReadOnlyList<TweakPlan> plans,
        string? settingsText,
        IReadOnlyList<Advice> health,
        IReadOnlyList<RunningApp> background,
        DateTime today)
    {
        var list = new List<Advice>();

        if (!gta.GameFound)
        {
            list.Add(new Advice(Severity.Critical, "GTA V не найдена",
                "Приложение не нашло GTA5.exe в реестре лаунчеров, библиотеках Steam и привычных папках. Если игра установлена в нестандартное место, режимы всё равно можно применить — они правят только settings.xml в «Документах»."));
        }

        if (!gta.SettingsExists)
        {
            list.Add(new Advice(Severity.Warning, "Нет файла настроек GTA V",
                "Запустите GTA V (или клиент проекта) один раз и закройте. Игра создаст settings.xml, после этого станут доступны режимы графики."));
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
            list.Add(new Advice(Severity.Info, "Гибридная графика",
                "На ноутбуке проверьте, что GTA V и клиент проекта идут на дискретной видеокарте: твик «GTA V и RAGE MP на мощной видеокарте» делает это за вас."));
        }

        if (s.Memory is { } mem && mem.TotalGb < 7.5)
        {
            list.Add(new Advice(Severity.Warning, $"Мало оперативной памяти: {mem.TotalGb:0.#} ГБ",
                "GTA V с клиентом проекта, Discord и браузером не помещается в 8 ГБ — Windows начинает подкачку и игра подвисает. Закройте браузер и лишние программы; режим «Ультра-слабый ПК» снижает расход."));
        }
        else if (s.Memory is { } mem2 && mem2.TotalGb < 15)
        {
            list.Add(new Advice(Severity.Info, $"Оперативной памяти {mem2.TotalGb:0.#} ГБ",
                "Для комфортной игры в РП вместе с Discord и браузером лучше 16 ГБ. На 8 ГБ закрывайте браузер перед игрой."));
        }

        if (s.Disks.Count > 0 && s.Disks.All(d => d.Kind == DiskKind.Hdd))
        {
            list.Add(new Advice(Severity.Warning, "GTA V установлена на обычном жёстком диске",
                "GTA V постоянно подгружает мир с диска. На HDD это даёт «подтормаживающие» текстуры и пустые участки на скорости. Перенос игры на SSD — самое заметное улучшение плавности, режимы графики его не заменят."));
        }

        if (s.Cpu is { } cpu && cpu.LogicalProcessors < 6)
        {
            list.Add(new Advice(Severity.Info, $"Процессор: {cpu.LogicalProcessors} потоков",
                "В РП-проектах нагрузка идёт в основном на процессор: много игроков, скриптов и объектов вокруг. На спавне и в людных местах FPS проседает сильнее всего — это не лечится настройками графики. Помогает сокращение дальности прорисовки (режимы «Слабый» и «Ультра-слабый»)."));
        }

        AddSettingsAdvice(list, gta, s, settingsText);

        foreach (var p in plans.Where(p => p.Status == TweakStatus.CanApply && p.RecommendedHere && !p.Tweak.Disputed))
        {
            var sev = p.Tweak.Effectiveness == Effectiveness.High ? Severity.Warning : Severity.Info;
            list.Add(new Advice(sev, $"Можно улучшить: {p.Tweak.Title}", $"{p.StatusText} Эффект: {p.Tweak.Effectiveness.Ru()}."));
        }

        foreach (var p in plans.Where(p => p.Status == TweakStatus.Blocked))
        {
            list.Add(new Advice(Severity.Warning, $"Сейчас недоступно: {p.Tweak.Title}", p.StatusText));
        }

        if (s.Display is { BelowMaxRefresh: true } d)
        {
            list.Add(new Advice(Severity.Warning, $"Монитор работает на {d.RefreshHz} Гц, а поддерживает {d.MaxRefreshHz} Гц",
                "Выберите максимальную частоту: Параметры → Система → Дисплей → Расширенные параметры дисплея. Картинка и управление станут заметно плавнее."));
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

    private static void AddSettingsAdvice(List<Advice> list, GtaInfo gta, SystemSnapshot s, string? settingsText)
    {
        if (settingsText is null)
        {
            return;
        }

        GtaSettingsFile file;
        try
        {
            file = GtaSettingsFile.Parse(settingsText);
        }
        catch (InvalidDataException)
        {
            list.Add(new Advice(Severity.Warning, "Файл настроек GTA V повреждён",
                "settings.xml не читается. Запустите GTA V и измените любую настройку графики — игра создаст файл заново."));
            return;
        }

        var gpu = s.PrimaryGpu;
        if (int.TryParse(file.Get("TextureQuality"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tex) && tex > GtaModes.TextureCap(gpu))
        {
            list.Add(new Advice(Severity.Warning, "Текстуры не помещаются в видеопамять",
                "Качество текстур выставлено выше, чем потянет память видеокарты: игра подвисает каждые несколько секунд, подгружая текстуры через оперативную память. Снизьте текстуры на ступень или включите режим по вашему ПК."));
        }

        if (int.TryParse(file.Get("MSAA"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var msaa) && msaa > 0)
        {
            list.Add(new Advice(Severity.Warning, $"Включено сглаживание MSAA ×{msaa}",
                "MSAA — самая дорогая настройка GTA V: она съедает до трети FPS. Для сглаживания достаточно FXAA, оно почти бесплатно."));
        }

        if (file.Is("MotionBlurStrength", "1") || (double.TryParse(file.Get("MotionBlurStrength"), NumberStyles.Float, CultureInfo.InvariantCulture, out var blur) && blur > 0.05))
        {
            list.Add(new Advice(Severity.Info, "Включено размытие движения", "В РП оно только мешает рассматривать людей и номера машин. Отключается в любом из режимов графики."));
        }

        if (file.Is("VSync", "1") && gta.SettingsExists)
        {
            list.Add(new Advice(Severity.Info, "Включена вертикальная синхронизация",
                "Она ограничивает FPS частотой монитора и добавляет задержку. Если у вас просадки ниже частоты монитора, V-Sync делает их заметнее; отключайте в режимах для слабого ПК."));
        }
    }
}
