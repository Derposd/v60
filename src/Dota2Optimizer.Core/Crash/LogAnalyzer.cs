using System.Text.RegularExpressions;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Crash;

public sealed record LogFinding(Severity Severity, string Title, string Advice, string Sample, int Count);

/// <summary>Looks for typical error signatures in a game's console.log and Steam logs. Pure logic.</summary>
public static class LogAnalyzer
{
    private sealed record Rule(Regex Pattern, Severity Severity, string Title, string Advice);

    private static readonly Rule[] Rules =
    {
        new(new Regex(@"DXGI_ERROR_DEVICE_(REMOVED|HUNG|RESET)|device removed", RegexOptions.IgnoreCase), Severity.Critical,
            "Видеокарта «отвалилась» во время игры (DEVICE_REMOVED/HUNG)",
            "Драйвер или видеокарта перестали отвечать: разгон, перегрев, питание или драйвер. Сбросьте разгон, обновите драйвер."),
        new(new Regex(@"VK_ERROR_DEVICE_LOST|vkQueueSubmit failed", RegexOptions.IgnoreCase), Severity.Critical,
            "Vulkan: потеря устройства (DEVICE_LOST)",
            "Попробуйте -dx11, обновите драйвер, сбросьте кеш шейдеров."),
        new(new Regex(@"out of memory|failed to allocate|allocation failed|E_OUTOFMEMORY", RegexOptions.IgnoreCase), Severity.Critical,
            "Нехватка памяти",
            "Закройте браузер, включите автоматический файл подкачки, снизьте качество текстур (mat_picmip)."),
        new(new Regex(@"D3D11CreateDevice.*fail|failed to create (d3d|device|swap ?chain)|CreateSwapChain.*fail", RegexOptions.IgnoreCase), Severity.Critical,
            "Не удалось создать устройство рендера",
            "Обновите драйвер, отключите оверлеи, попробуйте другой API (-dx11/-vulkan)."),
        new(new Regex(@"(failed to (open|read|load|mount)|corrupt|crc mismatch).*\.vpk|vpk.*(corrupt|crc)", RegexOptions.IgnoreCase), Severity.Warning,
            "Повреждённые файлы игры (.vpk)",
            "Проверьте целостность файлов игры (кнопка «Проверить файлы»)."),
        new(new Regex(@"disk write failure|disk read error|corrupt (update )?files", RegexOptions.IgnoreCase), Severity.Warning,
            "Ошибки чтения/записи диска (Steam)",
            "Проверьте диск (chkdsk), свободное место, антивирус (он может блокировать запись). Затем проверьте целостность файлов."),
        new(new Regex(@"connection to steam servers lost|lost connection to steam|SteamNetworkingSockets.*(timed out|problem)", RegexOptions.IgnoreCase), Severity.Warning,
            "Обрывы связи со Steam",
            "Проверьте вкладку «Сеть»: потери пакетов до шлюза или провайдера."),
        new(new Regex(@"NETWORK_DISCONNECT|connection timed out|timed out waiting", RegexOptions.IgnoreCase), Severity.Warning,
            "Отключения от игрового сервера",
            "Проверьте вкладку «Сеть». Если потерь нет — проблема может быть на стороне маршрута или сервера."),
        new(new Regex(@"unknown command|unknown convar|bad command", RegexOptions.IgnoreCase), Severity.Info,
            "Неизвестные команды в конфиге",
            "В autoexec.cfg есть устаревшие команды. Они не опасны, но не работают — удалите их."),
        new(new Regex(@"shader.*(compil|cache).*(fail|error)", RegexOptions.IgnoreCase), Severity.Warning,
            "Ошибки компиляции/кеша шейдеров",
            "Сбросьте кеш шейдеров (кнопка ниже) и перезапустите игру."),
    };

    public static IReadOnlyList<LogFinding> Analyze(string text)
    {
        var lines = text.Split('\n');
        var result = new List<LogFinding>();
        foreach (var rule in Rules)
        {
            var hits = lines.Where(l => rule.Pattern.IsMatch(l)).ToList();
            if (hits.Count > 0)
            {
                var sample = hits[^1].Trim();
                result.Add(new LogFinding(rule.Severity, rule.Title, rule.Advice, sample.Length > 200 ? sample[..200] + "…" : sample, hits.Count));
            }
        }

        return result.OrderByDescending(r => r.Severity).ToList();
    }
}
