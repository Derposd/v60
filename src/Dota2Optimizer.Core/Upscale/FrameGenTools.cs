using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Upscale;

/// <summary>Lossless Scaling (Steam app 993090): found through the Steam libraries, launched through Steam.</summary>
public static class LosslessScaling
{
    public const string AppId = "993090";
    public const string StoreUrl = "https://store.steampowered.com/app/993090/Lossless_Scaling/";

    public static string? Find(IFileSystem fs, IEnumerable<string> steamLibraries)
    {
        foreach (var lib in steamLibraries)
        {
            var exe = Path.Combine(lib, "steamapps", "common", "Lossless Scaling", "LosslessScaling.exe");
            if (fs.FileExists(Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf")) && fs.FileExists(exe))
            {
                return exe;
            }
        }

        return null;
    }

    /// <summary>Settings to start from, chosen for the card and the monitor. Lossless Scaling keeps them per game in its own profiles.</summary>
    public static IReadOnlyList<string> Recommendations(GpuFamily gpu, int refreshHz)
    {
        var hz = refreshHz > 0 ? refreshHz : 60;
        var weak = gpu.Integrated || (gpu.Vendor == GpuVendor.Nvidia && gpu.RtxGen == 0 && gpu.GtxModel is > 0 and < 1660) || (gpu.Vendor == GpuVendor.Amd && gpu.RdnaSeries == 0);
        var list = new List<string>
        {
            "Игра — в окне или «окне без рамки»; в Lossless Scaling нажмите «Масштабировать» (по умолчанию Ctrl+Alt+S) и вернитесь в игру за 5 секунд.",
            $"Ограничьте FPS в самой игре до {hz / 2} (половина частоты монитора {hz} Гц) — тогда ×2 даст ровные {hz} кадров. Без лимита генерация «плавает».",
        };
        list.Add(weak
            ? "Слабая или встроенная видеокарта: Frame Generation — LSFG 3, режим Performance, множитель ×2; масштабирование выключите или «LS1» с низкой резкостью. Если FPS игры падает ниже 30, генерация не поможет."
            : "Frame Generation — LSFG 3, множитель ×2 (×3 только при 60+ исходных FPS), режим качества — по умолчанию.");
        list.Add("Генерация кадров добавляет задержку (обычно один-два исходных кадра). В соревновательных онлайн-играх её лучше не включать.");
        return list;
    }
}

/// <summary>Anti-cheats recognised by files in the game folder. A game with one of these never gets OptiScaler installed.</summary>
public static class AntiCheatScanner
{
    private static readonly (string Name, string[] Markers)[] Known =
    {
        ("Easy Anti-Cheat", new[] { "EasyAntiCheat", "EasyAntiCheat_EOS.sys", "EasyAntiCheat_x64.dll", "start_protected_game.exe" }),
        ("BattlEye", new[] { "BattlEye", "BEService_x64.exe", "BEClient_x64.dll" }),
        ("Riot Vanguard", new[] { "vgc.exe", "vgk.sys", "Riot Vanguard" }),
        ("EA AntiCheat", new[] { "EAAntiCheat.GameServiceLauncher.exe", "EAAntiCheat" }),
        ("PunkBuster", new[] { "pbsvc.exe", "PnkBstrA.exe", "pb" }),
        ("XIGNCODE3", new[] { "xigncode", "x3.xem" }),
        ("nProtect GameGuard", new[] { "GameGuard", "GameMon.des" }),
        ("Tencent ACE", new[] { "ACE-Base.sys", "AntiCheatExpert" }),
        ("Ricochet (Call of Duty)", new[] { "randgrid.sys", "ricochet" }),
        ("FACEIT", new[] { "faceit" }),
        ("Denuvo Anti-Cheat", new[] { "denuvo-anticheat", "anti_cheat_runtime" }),
        ("EQU8", new[] { "equ8" }),
        ("mhyprot (HoYoverse)", new[] { "mhyprot2.sys", "mhyprot3.sys" }),
    };

    /// <summary>Folder names are checked one level deep around the exe and in the game root; files by exact name.</summary>
    public static IReadOnlyList<string> Scan(IFileSystem fs, string gameDir, int depth = 2)
    {
        var found = new List<string>();
        var names = Names(fs, gameDir, depth);
        foreach (var (name, markers) in Known)
        {
            if (markers.Any(m => names.Contains(m)))
            {
                found.Add(name);
            }
        }

        return found;
    }

    private static HashSet<string> Names(IFileSystem fs, string dir, int depth)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string d, int level)
        {
            foreach (var f in fs.EnumerateFiles(d, "*", recursive: false))
            {
                set.Add(PathEx.FileName(f));
            }

            if (level >= depth)
            {
                return;
            }

            foreach (var sub in fs.EnumerateDirectories(d))
            {
                set.Add(PathEx.FileName(sub));
                Walk(sub, level + 1);
            }
        }

        Walk(dir, 0);
        return set;
    }
}

/// <summary>Upscaler libraries a game ships with: OptiScaler can only replace an upscaler the game already has.</summary>
public static class UpscalerDlls
{
    private static readonly (string File, string Name)[] Known =
    {
        ("nvngx_dlss.dll", "DLSS"),
        ("libxess.dll", "XeSS"),
        ("libxess_dx11.dll", "XeSS (DX11)"),
        ("ffx_fsr2_api_x64.dll", "FSR 2"),
        ("ffx_fsr2_api_dx12_x64.dll", "FSR 2 (DX12)"),
        ("ffx_fsr2_api_vk_x64.dll", "FSR 2 (Vulkan)"),
        ("amd_fidelityfx_dx12.dll", "FSR 3"),
        ("amd_fidelityfx_vk.dll", "FSR 3 (Vulkan)"),
        ("nvngx_dlssg.dll", "DLSS Frame Generation"),
    };

    public static IReadOnlyList<string> Find(IFileSystem fs, string gameDir) =>
        Known.Where(k => fs.EnumerateFiles(gameDir, k.File, recursive: true).Any()).Select(k => k.Name).Distinct().ToList();
}

public sealed record OptiScalerPlan(IReadOnlyList<PlannedAction> Actions, string ProxyName, IReadOnlyList<string> Upscalers, IReadOnlyList<string> AntiCheats, string? Blocked);

/// <summary>
/// Helper for OptiScaler (GPLv3, github.com/optiscaler/OptiScaler). It is not bundled: the official release is downloaded on request,
/// then its files are copied next to the game exe with a backup of anything they would replace, so «Удалить» puts the game back exactly.
/// </summary>
public static class OptiScalerInstaller
{
    public const string Repo = "optiscaler/OptiScaler";
    public const string TweakPrefix = "optiscaler:";

    /// <summary>Names OptiScaler can be loaded as; the first one the game doesn't already use is chosen (so ReShade's dxgi.dll stays).</summary>
    public static readonly string[] ProxyNames = { "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", "wininet.dll", "winhttp.dll" };

    /// <summary>Support libraries from the release that are copied only if the game doesn't ship its own copy.</summary>
    private static readonly string[] OnlyIfMissing = { "libxess.dll", "libxess_dx11.dll", "amd_fidelityfx_dx12.dll", "amd_fidelityfx_vk.dll", "amd_fidelityfx_upscaler_dx12.dll", "amd_fidelityfx_framegeneration_dx12.dll" };

    /// <summary>Online games protected server-side (VAC and others) that leave no anti-cheat files to find.</summary>
    public static readonly HashSet<string> BlockedExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2.exe", "dota2.exe", "csgo.exe", "r5apex.exe", "r5apex_dx12.exe", "FortniteClient-Win64-Shipping.exe", "VALORANT-Win64-Shipping.exe",
        "TslGame.exe", "RainbowSix.exe", "Overwatch.exe", "cod.exe", "RustClient.exe", "destiny2.exe", "GTA5.exe", "GTA5_Enhanced.exe",
        "League of Legends.exe", "deadlock.exe", "project8.exe",
    };

    public static string TweakId(string gameExe) => TweakPrefix + gameExe.ToLowerInvariant();

    public static OptiScalerPlan Plan(IFileSystem fs, string gameExe, string releaseDir)
    {
        var gameDir = PathEx.Parent(gameExe) ?? throw new InvalidOperationException("Нет папки игры");
        if (BlockedExes.Contains(PathEx.FileName(gameExe)))
        {
            return new OptiScalerPlan(Array.Empty<PlannedAction>(), string.Empty, Array.Empty<string>(), new[] { "онлайн-игра с античитом" },
                $"{PathEx.FileName(gameExe)} — онлайн-игра с античитом. Внедрение DLL почти наверняка приведёт к бану — установка заблокирована.");
        }

        var root = GameRoot(fs, gameDir);
        var antiCheats = AntiCheatScanner.Scan(fs, root);
        var upscalers = UpscalerDlls.Find(fs, root);
        if (antiCheats.Count > 0)
        {
            return new OptiScalerPlan(Array.Empty<PlannedAction>(), string.Empty, upscalers, antiCheats,
                $"В игре найден античит: {string.Join(", ", antiCheats)}. Внедрение DLL в такую игру почти наверняка приведёт к бану — установка заблокирована.");
        }

        if (upscalers.Count == 0)
        {
            return new OptiScalerPlan(Array.Empty<PlannedAction>(), string.Empty, upscalers, antiCheats,
                "В папке игры нет DLSS, FSR 2+ или XeSS — OptiScaler заменяет только тот апскейлер, который в игре уже есть. Попробуйте оконный апскейлер или Lossless Scaling.");
        }

        var main = fs.EnumerateFiles(releaseDir, "OptiScaler.dll", recursive: true).FirstOrDefault()
                   ?? throw new InvalidOperationException("В архиве нет OptiScaler.dll — это не релиз OptiScaler.");
        var srcRoot = PathEx.Parent(main)!;
        var proxy = ProxyNames.FirstOrDefault(n => !fs.FileExists(PathEx.Join(gameDir, n)))
                    ?? throw new InvalidOperationException("Все имена для загрузки OptiScaler уже заняты другими модами.");
        var actions = new List<PlannedAction> { PlannedAction.CopyFile(main, PathEx.Join(gameDir, proxy), $"OptiScaler как {proxy}") };
        foreach (var file in fs.EnumerateFiles(srcRoot, "*", recursive: true))
        {
            var rel = PathEx.Relative(srcRoot, file);
            var name = PathEx.FileName(file);
            if (name.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("Licenses", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = PathEx.Join(gameDir, rel);
            if (OnlyIfMissing.Contains(name, StringComparer.OrdinalIgnoreCase) && fs.FileExists(target))
            {
                continue; // the game's own copy wins
            }

            actions.Add(PlannedAction.CopyFile(file, target, $"OptiScaler: {rel}"));
        }

        return new OptiScalerPlan(actions, proxy, upscalers, antiCheats, null);
    }

    /// <summary>The game's top folder (for Steam: steamapps\common\&lt;Game&gt;), where anti-cheat folders usually live.</summary>
    public static string GameRoot(IFileSystem fs, string exeDir)
    {
        var dir = exeDir;
        for (var i = 0; i < 4; i++)
        {
            var parent = PathEx.Parent(dir);
            if (parent is null)
            {
                break;
            }

            if (PathEx.FileName(parent).Equals("common", StringComparison.OrdinalIgnoreCase))
            {
                return dir;
            }

            dir = parent;
        }

        // Not under a Steam library: two levels up from Binaries\Win64 (Unreal) or the exe folder itself.
        var name = PathEx.FileName(exeDir);
        return name.Equals("Win64", StringComparison.OrdinalIgnoreCase) && PathEx.Parent(exeDir) is { } bin && PathEx.Parent(bin) is { } project
            ? PathEx.Parent(project) ?? exeDir
            : exeDir;
    }
}

public sealed record ReleaseAsset(string Tag, string Name, string Url, long Size, string PageUrl);

/// <summary>Reads the latest official release from the GitHub API — only when the user presses «Скачать».</summary>
public static class GitHubReleases
{
    private sealed class Release
    {
        [JsonPropertyName("tag_name")]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string Page { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<Asset> Assets { get; set; } = new();
    }

    private sealed class Asset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public static async Task<ReleaseAsset?> LatestAsync(HttpClient http, string repo, Func<string, bool> pick, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.UserAgent.ParseAdd("FrameLab");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<Release>(cancellationToken: ct);
        var a = r?.Assets.FirstOrDefault(x => pick(x.Name));
        return r is null || a is null ? null : new ReleaseAsset(r.Tag, a.Name, a.Url, a.Size, r.Page);
    }

    /// <summary>Downloads to a file; only github.com / objects.githubusercontent.com URLs are accepted.</summary>
    public static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string path, IProgress<double>? progress, CancellationToken ct)
    {
        var uri = new Uri(asset.Url);
        if (uri.Scheme != Uri.UriSchemeHttps || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Недопустимый адрес загрузки: {uri.Host}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path + ".part");
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            total += n;
            if (asset.Size > 0)
            {
                progress?.Report((double)total / asset.Size);
            }
        }

        await dst.FlushAsync(ct);
        dst.Close();
        if (asset.Size > 0 && total != asset.Size)
        {
            File.Delete(path + ".part");
            throw new IOException($"Файл скачался не полностью ({total} из {asset.Size} байт).");
        }

        File.Move(path + ".part", path, overwrite: true);
    }
}

/// <summary>An OptiScaler install as a tweak, so it gets the same preview, backup and «Удалить» (= undo) as everything else.</summary>
public sealed class OptiScalerTweak : Tweaks.Tweak
{
    public OptiScalerTweak(string gameExe, OptiScalerPlan plan)
    {
        GameExe = gameExe;
        Install = plan;
    }

    public string GameExe { get; }
    public OptiScalerPlan Install { get; }

    public override string Id => OptiScalerInstaller.TweakId(GameExe);
    public override string Title => $"OptiScaler для {PathEx.FileName(GameExe)}";
    public override Tweaks.TweakCategory Category => Tweaks.TweakCategory.Game;
    public override Tweaks.Effectiveness Effectiveness => Tweaks.Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override Tweaks.TweakRisk RiskLevel => Tweaks.TweakRisk.High;
    public override string Explanation => "Сторонний мод: подменяет апскейлер игры (DLSS → FSR/XeSS) и добавляет генерацию кадров OptiFG в DX12.";

    public override Tweaks.TweakPlan Plan(Tweaks.TweakContext ctx) =>
        Install.Blocked is { } why
            ? Result(Tweaks.TweakStatus.Blocked, why, recommendedHere: false)
            : Result(Tweaks.TweakStatus.CanApply, $"Файлов: {Install.Actions.Count}, загрузка как {Install.ProxyName}.", Install.Actions, recommendedHere: false);
}
