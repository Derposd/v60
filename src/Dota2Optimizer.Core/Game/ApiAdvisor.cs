using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Game;

public sealed record ApiRecommendation(string Token, string Title, string Reason, string HowToCompare);

/// <summary>
/// DX11 vs Vulkan. Public data is thin and old, so this is a starting point, not a verdict:
/// the user is always told to measure both.
/// </summary>
public static class ApiAdvisor
{
    public const string HowToCompare =
        "1) Выберите -dx11, примените, зайдите в демо-режим героя (Герои → выбрать героя → «Демо героя») или в повтор одного и того же матча.\n" +
        "2) Включите счётчик: Настройки → Опции → Расширенные → «Показывать информацию о сети/FPS», либо консоль: cl_showfps 1 (нужен -console).\n" +
        "3) Замерьте 2–3 минуты в одинаковой сцене: средний FPS и минимум в драке. Или используйте вкладку «Замер FPS».\n" +
        "4) Повторите с -vulkan. Первый запуск Vulkan после обновления драйвера может фризить, пока строится кеш шейдеров — замеряйте со второго захода.\n" +
        "5) Оставьте вариант с более высоким минимальным FPS (1% low), а не только средним.";

    public static ApiRecommendation Recommend(GpuInfo? gpu, CpuInfo? cpu)
    {
        if (gpu is null)
        {
            return new ApiRecommendation("-dx11", "DirectX 11", "Видеокарта не определена — DX11 самый совместимый вариант.", HowToCompare);
        }

        var weakCpu = cpu is not null && cpu.LogicalProcessors <= 4;
        return gpu.Vendor switch
        {
            GpuVendor.Amd => new ApiRecommendation("-vulkan", "Vulkan",
                "На Radeon в Dota 2 Vulkan обычно меньше нагружает процессор, чем DX11. Но AMD Anti-Lag 2 (снижение задержки, Radeon RX 5000 и новее) поддерживается в Dota 2 в режиме DX11 — если для вас важнее задержка, сравните оба варианта. При фризах или чёрных экранах вернитесь на DX11.", HowToCompare),
            GpuVendor.Intel when gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase) => new ApiRecommendation("-vulkan", "Vulkan",
                "Для Intel Arc часто сообщают о более ровных кадрах на Vulkan, но данные разрозненные — сравните оба.", HowToCompare),
            GpuVendor.Intel => new ApiRecommendation("-dx11", "DirectX 11",
                "Для встроенной графики Intel DX11 обычно стабильнее. Vulkan стоит проверить на свежем драйвере.", HowToCompare),
            GpuVendor.Nvidia when weakCpu => new ApiRecommendation("-vulkan", "Vulkan (проверить)",
                "Процессор с 4 и менее потоками: Vulkan может снизить нагрузку на CPU. На NVIDIA разница обычно небольшая — сравните.", HowToCompare),
            _ => new ApiRecommendation("-dx11", "DirectX 11",
                "На NVIDIA оба API работают хорошо; DX11 — значение по умолчанию и самый стабильный вариант. Vulkan можно проверить.", HowToCompare),
        };
    }
}
