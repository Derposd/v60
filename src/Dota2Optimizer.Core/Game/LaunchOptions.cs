using System.Globalization;
using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Tweaks;
using Dota2Optimizer.Core.Vdf;

namespace Dota2Optimizer.Core.Game;

public sealed record LaunchOptionInfo(
    string Token,
    string Title,
    string Explanation,
    Effectiveness Effectiveness,
    bool Recommended,
    bool Disputed);

/// <summary>Known Steam launch options for Dota 2, checked against public references in October 2026.</summary>
public static class LaunchOptionCatalog
{
    public static readonly LaunchOptionInfo NoVid = new("-novid", "Пропуск заставки",
        "Не показывает вступительное видео при запуске. На FPS не влияет — только экономит секунды при старте.",
        Effectiveness.Low, Recommended: true, Disputed: false);

    public static readonly LaunchOptionInfo Dx11 = new("-dx11", "Рендер DirectX 11",
        "Принудительно DirectX 11. Обычно самый стабильный вариант на NVIDIA и Intel. Какой API быстрее — зависит от карты и драйвера, проверяйте оба.",
        Effectiveness.HardwareDependent, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo Vulkan = new("-vulkan", "Рендер Vulkan",
        "Принудительно Vulkan. Часто даёт меньшую нагрузку на процессор (особенно на AMD), но после обновлений драйвера возможны фризы компиляции шейдеров в первых матчах.",
        Effectiveness.HardwareDependent, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo FpsMax = new("+fps_max", "Ограничение FPS",
        "Ограничивает кадры в секунду. На ноутбуках и слабых ПК уменьшает нагрев и троттлинг, делает кадры ровнее. Значение подбирается под частоту монитора.",
        Effectiveness.Medium, Recommended: true, Disputed: false);

    public static readonly LaunchOptionInfo High = new("-high", "Высокий приоритет процесса",
        "Запускает игру с высоким приоритетом. Проверенных тестов с выигрышем нет; при нехватке ядер может мешать звуку, Discord и стримингу. Включайте только если сами проверили замером.",
        Effectiveness.Low, Recommended: false, Disputed: true);

    public static readonly LaunchOptionInfo NoJoy = new("-nojoy", "Отключить геймпад",
        "Не инициализирует поддержку джойстиков. Эффект на производительность пренебрежимо мал.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo Console = new("-console", "Консоль разработчика",
        "Включает консоль (клавиша ~). Нужна для ручного замера FPS (cl_showfps 1) и диагностики. На производительность не влияет.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static readonly LaunchOptionInfo ConDebug = new("-condebug", "Писать console.log",
        "Сохраняет вывод консоли в game\\dota\\console.log — по нему вкладка «Диагностика вылетов» ищет ошибки. Включайте на время поиска проблемы.",
        Effectiveness.Low, Recommended: false, Disputed: false);

    public static IReadOnlyList<LaunchOptionInfo> All { get; } = new[] { NoVid, Dx11, Vulkan, FpsMax, High, NoJoy, Console, ConDebug };

    /// <summary>
    /// Options we deliberately do not offer: removed by Valve, belong to other Source games or never did
    /// anything measurable. If found in the user's line we suggest removing them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Myths { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["-threads"] = "движок сам определяет число потоков; ручное значение может только навредить",
        ["-prewarm"] = "удалён Valve, его эффект теперь включён по умолчанию",
        ["-preload"] = "нет подтверждений, что работает в Source 2",
        ["-map"] = "«-map dota» не ускоряет загрузку матча в современной Dota 2, может удлинить запуск",
        ["-dxlevel"] = "параметр DirectX 9 из старого Source, в Dota 2 не работает",
        ["-heapsize"] = "параметр старого Source, в 64-битном Source 2 не используется",
        ["-useforcedmparms"] = "параметр мыши из старого Source, в Dota 2 не работает",
        ["-noforcemaccel"] = "параметр мыши из старого Source, в Dota 2 не работает",
        ["-noforcemspd"] = "параметр мыши из старого Source, в Dota 2 не работает",
        ["-noaafonts"] = "в Source 2 не действует",
        ["-USEALLAVAILABLECORES"] = "миф: движок и так использует ядра",
        ["-softparticlesdefaultoff"] = "параметр старого Source, в Dota 2 не работает",
        ["-dx9"] = "DirectX 9 удалён из Dota 2 в 2021 году",
        ["-gl"] = "OpenGL удалён из Dota 2, используйте -vulkan",
        ["-dx12"] = "нет подтверждений поддержки DirectX 12 в Dota 2",
    };
}

public sealed record LaunchOptionsAnalysis(
    IReadOnlyList<string> ManagedTokens,
    IReadOnlyList<string> UserTokens,
    IReadOnlyList<(string Token, string Reason)> MythTokens);

/// <summary>Builds a launch options line, keeping anything the user added that we don't manage.</summary>
public static class LaunchOptionsComposer
{
    public static IReadOnlyList<string> Tokenize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Array.Empty<string>();
        }

        // Group "+cvar value" pairs and "-option value" pairs where the value is not an option itself.
        var raw = Regex.Matches(line, "\"[^\"]*\"|\\S+").Select(m => m.Value).ToList();
        var tokens = new List<string>();
        for (var i = 0; i < raw.Count; i++)
        {
            var t = raw[i];
            var isCvar = t.StartsWith('+');
            var takesValue = isCvar || t.Equals("-threads", StringComparison.OrdinalIgnoreCase) || t.Equals("-map", StringComparison.OrdinalIgnoreCase)
                             || t.Equals("-dxlevel", StringComparison.OrdinalIgnoreCase) || t.Equals("-heapsize", StringComparison.OrdinalIgnoreCase);
            if (takesValue && i + 1 < raw.Count && !raw[i + 1].StartsWith('-') && !raw[i + 1].StartsWith('+'))
            {
                tokens.Add(t + " " + raw[i + 1]);
                i++;
            }
            else
            {
                tokens.Add(t);
            }
        }

        return tokens;
    }

    private static string Head(string token) => token.Split(' ')[0];

    public static LaunchOptionsAnalysis Analyze(string? line)
    {
        var managedHeads = LaunchOptionCatalog.All.Select(o => o.Token).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = new List<string>();
        var user = new List<string>();
        var myths = new List<(string, string)>();
        foreach (var t in Tokenize(line))
        {
            var head = Head(t);
            if (managedHeads.Contains(head))
            {
                managed.Add(t);
            }
            else if (LaunchOptionCatalog.Myths.TryGetValue(head, out var reason))
            {
                myths.Add((t, reason));
            }
            else
            {
                user.Add(t);
            }
        }

        return new LaunchOptionsAnalysis(managed, user, myths);
    }

    /// <param name="current">Current line from Steam.</param>
    /// <param name="selected">Managed options the user wants (tokens from the catalog).</param>
    /// <param name="fpsMax">Value for +fps_max when selected; ignored otherwise.</param>
    /// <param name="removeMyths">Drop known-useless options found in the current line.</param>
    public static string Compose(string? current, IEnumerable<string> selected, int? fpsMax, bool removeMyths)
    {
        var analysis = Analyze(current);
        var result = new List<string>();
        foreach (var token in selected.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (token.Equals(LaunchOptionCatalog.FpsMax.Token, StringComparison.OrdinalIgnoreCase))
            {
                if (fpsMax is { } fps)
                {
                    result.Add($"+fps_max {fps.ToString(CultureInfo.InvariantCulture)}");
                }
            }
            else
            {
                result.Add(token);
            }
        }

        // -dx11 and -vulkan are mutually exclusive; the later choice in the catalog order is a user error.
        if (result.Contains("-dx11", StringComparer.OrdinalIgnoreCase) && result.Contains("-vulkan", StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Нельзя одновременно выбрать -dx11 и -vulkan");
        }

        if (!removeMyths)
        {
            result.AddRange(analysis.MythTokens.Select(m => m.Token));
        }

        result.AddRange(analysis.UserTokens);
        return string.Join(' ', result);
    }

    /// <summary>fps_max suggestion: match the refresh rate on weak PCs/laptops, a little above it otherwise.</summary>
    public static int SuggestFpsMax(int refreshHz, bool weakOrLaptop, bool variableRefresh)
    {
        var hz = refreshHz <= 0 ? 60 : refreshHz;
        if (variableRefresh)
        {
            // With G-Sync/FreeSync stay a few frames under the refresh rate so VRR never disengages.
            return Math.Max(30, hz - 3);
        }

        return weakOrLaptop ? hz : (int)Math.Round(hz * 1.5);
    }
}

/// <summary>Reads and writes Dota 2 launch options in Steam's localconfig.vdf.</summary>
public static class LocalConfigEditor
{
    private static VdfNode? AppsNode(VdfNode root) =>
        root.Path("Software", "Valve", "Steam", "apps") ?? root.Path("Software", "Valve", "Steam", "Apps");

    public static string? GetLaunchOptions(string localConfigText)
    {
        var root = VdfParser.Parse(localConfigText);
        return AppsNode(root)?.Child(SteamLocator.DotaAppId)?.ValueOf("LaunchOptions");
    }

    public static string SetLaunchOptions(string localConfigText, string launchOptions)
    {
        var root = VdfParser.Parse(localConfigText);
        var apps = AppsNode(root) ?? root.GetOrAddObject("Software").GetOrAddObject("Valve").GetOrAddObject("Steam").GetOrAddObject("apps");
        var app = apps.GetOrAddObject(SteamLocator.DotaAppId);
        app.SetValue("LaunchOptions", launchOptions);
        return root.Serialize();
    }
}
