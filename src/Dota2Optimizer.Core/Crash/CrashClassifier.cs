using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Crash;

public sealed record RawEvent(DateTime Time, string Log, string Provider, int Id, string Message, IReadOnlyList<string> Properties);

public sealed record CrashFinding(Severity Severity, string Category, string Title, string Advice, DateTime LastSeen, int Count);

/// <summary>Turns Windows event log entries into human-readable causes. Pure logic, unit-tested.</summary>
public static class CrashClassifier
{
    private static readonly (string Module, string Category, string Advice)[] ModuleHints =
    {
        ("nvwgf2umx", "Драйвер NVIDIA", "Сбой в драйвере NVIDIA (DirectX). Обновите или переустановите драйвер начисто (галочка «Чистая установка»), сбросьте разгон видеокарты, сбросьте кеш шейдеров."),
        ("nvoglv64", "Драйвер NVIDIA", "Сбой в драйвере NVIDIA (Vulkan/OpenGL). Попробуйте -dx11, обновите драйвер, сбросьте кеш шейдеров."),
        ("nvldumdx", "Драйвер NVIDIA", "Сбой в драйвере NVIDIA. Обновите драйвер, сбросьте разгон."),
        ("nvgpucomp", "Драйвер NVIDIA", "Сбой компилятора шейдеров NVIDIA. Сбросьте кеш шейдеров и обновите драйвер."),
        ("atidxx64", "Драйвер AMD", "Сбой в драйвере AMD (DirectX). Обновите драйвер (при проблемах — через AMD Cleanup Utility), сбросьте разгон/андервольт."),
        ("amdxx64", "Драйвер AMD", "Сбой в драйвере AMD (DirectX 11). Обновите драйвер, отключите Enhanced Sync, сбросьте разгон."),
        ("amdvlk64", "Драйвер AMD", "Сбой в Vulkan-драйвере AMD. Попробуйте -dx11, обновите драйвер."),
        ("amdxc64", "Драйвер AMD", "Сбой компилятора шейдеров AMD. Сбросьте кеш шейдеров, обновите драйвер."),
        ("igd10iumd64", "Драйвер Intel", "Сбой в драйвере Intel. Обновите драйвер с сайта Intel или производителя ноутбука."),
        ("igdumdim64", "Драйвер Intel", "Сбой в драйвере Intel. Обновите драйвер."),
        ("igvk64", "Драйвер Intel", "Сбой в Vulkan-драйвере Intel. Попробуйте -dx11, обновите драйвер."),
        ("vulkan-1", "Vulkan", "Сбой в загрузчике Vulkan. Переустановите драйвер видеокарты или используйте -dx11."),
        ("d3d11", "DirectX", "Сбой в DirectX 11 — обычно из-за драйвера видеокарты или оверлея. Обновите драйвер, отключите оверлеи."),
        ("dxgi", "DirectX", "Сбой в DXGI — часто оверлей или запись экрана. Отключите оверлеи (Discord, Afterburner, GeForce/NVIDIA App)."),
        ("rtsshooks", "Оверлей", "Сбой в оверлее RivaTuner (MSI Afterburner). Закройте RTSS или добавьте Dota 2 в исключения."),
        ("discordhook", "Оверлей", "Сбой в оверлее Discord. Отключите внутриигровой оверлей Discord для Dota 2."),
        ("gameoverlayrenderer", "Оверлей", "Сбой в оверлее Steam. Попробуйте отключить оверлей Steam для Dota 2."),
        ("nvspcap", "Оверлей", "Сбой в записи NVIDIA (ShadowPlay/Instant Replay). Отключите оверлей NVIDIA App."),
        ("ucrtbase", "Visual C++", "Сбой в среде выполнения C++. Переустановите Microsoft Visual C++ Redistributable 2015–2022 (x64)."),
        ("vcruntime140", "Visual C++", "Сбой в среде выполнения C++. Переустановите Microsoft Visual C++ Redistributable 2015–2022 (x64)."),
        ("msvcp140", "Visual C++", "Сбой в среде выполнения C++. Переустановите Microsoft Visual C++ Redistributable 2015–2022 (x64)."),
        ("engine2", "Файлы игры", "Сбой в движке игры. Проверьте целостность файлов; если вылеты после патча у многих — ждите исправления от Valve."),
        ("client", "Файлы игры", "Сбой в клиенте игры. Проверьте целостность файлов, удалите autoexec.cfg с экзотическими командами."),
        ("rendersystem", "Рендер", "Сбой в системе рендера игры. Смените API (-dx11 / -vulkan), обновите драйвер, сбросьте кеш шейдеров."),
    };

    public static IReadOnlyList<CrashFinding> Classify(IEnumerable<RawEvent> events)
    {
        var findings = new List<(Severity Sev, string Cat, string Title, string Advice, DateTime Time)>();
        foreach (var e in events)
        {
            var f = ClassifyOne(e);
            if (f is not null)
            {
                findings.Add((f.Value.Sev, f.Value.Cat, f.Value.Title, f.Value.Advice, e.Time));
            }
        }

        return findings
            .GroupBy(f => (f.Cat, f.Title))
            .Select(g => new CrashFinding(g.Max(x => x.Sev), g.Key.Cat, g.Key.Title, g.First().Advice, g.Max(x => x.Time), g.Count()))
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.LastSeen)
            .ToList();
    }

    private static (Severity Sev, string Cat, string Title, string Advice)? ClassifyOne(RawEvent e)
    {
        var msg = e.Message ?? string.Empty;
        var provider = e.Provider ?? string.Empty;

        if (e.Log == "Application" && e.Id == 1000)
        {
            var app = Prop(e, 0) ?? msg;
            if (!app.Contains("dota2", StringComparison.OrdinalIgnoreCase) && !msg.Contains("dota2.exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var module = (Prop(e, 3) ?? string.Empty).ToLowerInvariant();
            var code = Prop(e, 6) ?? string.Empty;
            var hint = ModuleHints.FirstOrDefault(h => module.StartsWith(h.Module, StringComparison.OrdinalIgnoreCase));
            if (hint.Module is not null)
            {
                return (Severity.Critical, hint.Category, $"Вылет Dota 2 в модуле {module}", hint.Advice);
            }

            if (code.Contains("c000001d", StringComparison.OrdinalIgnoreCase))
            {
                return (Severity.Critical, "Процессор/разгон", "Вылет Dota 2: недопустимая инструкция (0xc000001d)",
                    "Часто признак нестабильного разгона/андервольта процессора. Верните настройки BIOS по умолчанию и проверьте снова.");
            }

            return (Severity.Critical, "Dota 2", $"Вылет Dota 2 (модуль {(module.Length > 0 ? module : "неизвестен")}, код {code})",
                "Проверьте целостность файлов игры, обновите драйвер видеокарты, отключите оверлеи и разгон. Если вылеты после патча — проверьте форумы Dota 2.");
        }

        if (e.Log == "Application" && e.Id == 1001)
        {
            if (msg.Contains("LiveKernelEvent", StringComparison.OrdinalIgnoreCase) && (msg.Contains(" 141", StringComparison.Ordinal) || msg.Contains(" 117", StringComparison.Ordinal)))
            {
                return (Severity.Critical, "Видеокарта (TDR)", "Видеокарта зависла и была перезапущена (LiveKernelEvent 141/117)",
                    "Драйвер не дождался ответа от видеокарты. Причины: нестабильный разгон/андервольт, перегрев, недостаток питания, старый драйвер. Сбросьте разгон, проверьте температуру, обновите драйвер.");
            }

            return msg.Contains("dota2", StringComparison.OrdinalIgnoreCase)
                ? (Severity.Warning, "Dota 2", "Отчёт об ошибке Windows для Dota 2", "Windows зафиксировала сбой или зависание игры. Смотрите соседние записи о модуле сбоя.")
                : null;
        }

        if (e.Log == "Application" && e.Id == 1002 && msg.Contains("dota2", StringComparison.OrdinalIgnoreCase))
        {
            return (Severity.Warning, "Dota 2", "Dota 2 перестала отвечать (зависание)",
                "Игра зависла и была закрыта. Частые причины: HDD, нехватка памяти, драйвер. Проверьте диск и файл подкачки.");
        }

        if (provider.Equals("Display", StringComparison.OrdinalIgnoreCase) && e.Id == 4101)
        {
            return (Severity.Critical, "Видеокарта (TDR)", "Видеодрайвер перестал отвечать и был восстановлен (Display 4101)",
                "Классический тайм-аут драйвера (TDR). Сбросьте разгон GPU и памяти, обновите драйвер начисто, проверьте температуру и блок питания. Не увеличивайте TdrDelay ради «лечения» — это скрывает причину.");
        }

        if (provider.Equals("nvlddmkm", StringComparison.OrdinalIgnoreCase))
        {
            return (Severity.Critical, "Драйвер NVIDIA", $"Ошибка ядра драйвера NVIDIA (nvlddmkm, событие {e.Id})",
                "Ошибки nvlddmkm обычно связаны с нестабильностью видеокарты: разгон, перегрев, питание, иногда конкретная версия драйвера. Сбросьте разгон, обновите или откатите драйвер.");
        }

        if (provider.StartsWith("amdkmdag", StringComparison.OrdinalIgnoreCase) || provider.StartsWith("amdwddmg", StringComparison.OrdinalIgnoreCase))
        {
            return (Severity.Critical, "Драйвер AMD", $"Ошибка драйвера AMD ({provider}, событие {e.Id})",
                "Типично для «чёрного экрана»/тайм-аута драйвера Radeon. Отключите Enhanced Sync и MPO-эксперименты, сбросьте разгон/андервольт, обновите драйвер через AMD Cleanup Utility.");
        }

        if (provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase) && e.Id == 41)
        {
            return (Severity.Critical, "Питание", "Неожиданная перезагрузка/выключение (Kernel-Power 41)",
                "Компьютер выключился без завершения работы. Если это происходит в игре — проверьте блок питания, температуры, разгон. Сама Dota 2 не может вызвать такую перезагрузку.");
        }

        if (provider.Contains("WHEA-Logger", StringComparison.OrdinalIgnoreCase))
        {
            return (Severity.Critical, "Оборудование", $"Аппаратная ошибка (WHEA, событие {e.Id})",
                "Процессор, память или шина PCIe сообщили об ошибке. Частая причина — разгон/андервольт или XMP на грани стабильности. Верните настройки BIOS по умолчанию и проверьте память (средство проверки памяти Windows).");
        }

        return null;
    }

    private static string? Prop(RawEvent e, int index) => index < e.Properties.Count ? e.Properties[index] : null;
}
