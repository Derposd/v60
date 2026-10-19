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

/// <summary>Launch options one game understands, plus the ones we call myths and offer to remove.</summary>
public sealed class LaunchCatalog
{
    public required IReadOnlyList<LaunchOptionInfo> All { get; init; }
    public required IReadOnlyDictionary<string, string> Myths { get; init; }

    /// <summary>"-option value" pairs (besides "+cvar value", which always take a value).</summary>
    public IReadOnlySet<string> ValueOptions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public required LaunchOptionInfo FpsMax { get; init; }

    /// <summary>Token that switches the renderer away from the default (DX11): "-vulkan".</summary>
    public string VulkanToken { get; init; } = "-vulkan";

    /// <summary>Token that forces DX11, if the game has one ("-dx11" in Dota 2; CS2 is DX11 without a flag).</summary>
    public string? Dx11Token { get; init; }

    public LaunchOptionInfo? Find(string token) => All.FirstOrDefault(o => o.Token.Equals(token, StringComparison.OrdinalIgnoreCase));
}

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

    public static readonly LaunchOptionInfo MapPreload = new("-map dota", "Загружать карту при запуске",
        "Загружает карту сразу при старте игры, чтобы после нахождения матча загрузка шла быстрее (заметнее на HDD). Запуск игры становится дольше. Свежих замеров для текущей версии нет — проверьте сами.",
        Effectiveness.HardwareDependent, Recommended: false, Disputed: true);

    public static IReadOnlyList<LaunchOptionInfo> All { get; } = new[] { NoVid, Dx11, Vulkan, FpsMax, High, MapPreload, NoJoy, Console, ConDebug };


    /// <summary>
    /// Options we deliberately do not offer: removed by Valve, belong to other Source games or never did
    /// anything measurable. If found in the user's line we suggest removing them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Myths { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["-threads"] = "движок сам определяет число потоков; ручное значение может только навредить",
        ["-prewarm"] = "удалён Valve, его эффект теперь включён по умолчанию",
        ["-preload"] = "нет подтверждений, что работает в Source 2",
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

    public static LaunchCatalog Dota { get; } = new()
    {
        All = All,
        Myths = Myths,
        ValueOptions = new HashSet<string>(new[] { "-threads", "-map", "-dxlevel", "-heapsize" }, StringComparer.OrdinalIgnoreCase),
        FpsMax = FpsMax,
        Dx11Token = Dx11.Token,
    };
}

public sealed record LaunchOptionsAnalysis(
    IReadOnlyList<string> ManagedTokens,
    IReadOnlyList<string> UserTokens,
    IReadOnlyList<(string Token, string Reason)> MythTokens);

/// <summary>Builds a launch options line, keeping anything the user added that we don't manage.</summary>
public static class LaunchOptionsComposer
{
    public static IReadOnlyList<string> Tokenize(string? line) => Tokenize(line, LaunchOptionCatalog.Dota);

    public static IReadOnlyList<string> Tokenize(string? line, LaunchCatalog catalog)
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
            var takesValue = isCvar || catalog.ValueOptions.Contains(t);
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

    public static LaunchOptionsAnalysis Analyze(string? line) => Analyze(line, LaunchOptionCatalog.Dota);

    public static LaunchOptionsAnalysis Analyze(string? line, LaunchCatalog catalog)
    {
        var managedHeads = catalog.All.Select(o => Head(o.Token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = new List<string>();
        var user = new List<string>();
        var myths = new List<(string, string)>();
        foreach (var t in Tokenize(line, catalog))
        {
            var head = Head(t);
            if (managedHeads.Contains(head))
            {
                managed.Add(t);
            }
            else if (catalog.Myths.TryGetValue(head, out var reason))
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
    public static string Compose(string? current, IEnumerable<string> selected, int? fpsMax, bool removeMyths) =>
        Compose(current, selected, fpsMax, removeMyths, LaunchOptionCatalog.Dota);

    public static string Compose(string? current, IEnumerable<string> selected, int? fpsMax, bool removeMyths, LaunchCatalog catalog)
    {
        var analysis = Analyze(current, catalog);
        var result = new List<string>();
        foreach (var token in selected.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (token.Equals(catalog.FpsMax.Token, StringComparison.OrdinalIgnoreCase))
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

        // -dx11 and -vulkan are mutually exclusive.
        if (catalog.Dx11Token is { } dx11 && result.Contains(dx11, StringComparer.OrdinalIgnoreCase) && result.Contains(catalog.VulkanToken, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Нельзя одновременно выбрать {dx11} и {catalog.VulkanToken}");
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

/// <summary>Reads and writes a game's launch options in Steam's localconfig.vdf.</summary>
public static class LocalConfigEditor
{
    private static VdfNode? AppsNode(VdfNode root) =>
        root.Path("Software", "Valve", "Steam", "apps") ?? root.Path("Software", "Valve", "Steam", "Apps");

    public static string? GetLaunchOptions(string localConfigText, string appId = SteamLocator.DotaAppId)
    {
        var root = VdfParser.Parse(localConfigText);
        return AppsNode(root)?.Child(appId)?.ValueOf("LaunchOptions");
    }

    public static string SetLaunchOptions(string localConfigText, string launchOptions, string appId = SteamLocator.DotaAppId)
    {
        var root = VdfParser.Parse(localConfigText);
        var apps = AppsNode(root) ?? root.GetOrAddObject("Software").GetOrAddObject("Valve").GetOrAddObject("Steam").GetOrAddObject("apps");
        var app = apps.GetOrAddObject(appId);
        app.SetValue("LaunchOptions", launchOptions);
        return root.Serialize();
    }
}
