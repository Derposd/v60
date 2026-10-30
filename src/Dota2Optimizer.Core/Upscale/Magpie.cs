using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Upscale;

public enum UpscaleAlgorithm
{
    Fsr,
    Nis,
    Cas,
    Lanczos,
    Integer,
    Anime4K,
}

public enum UpscalePreset
{
    Quality,
    Balance,
    Performance,
}

public sealed record MagpieRelease(string Version, string Url, string Md5);

/// <summary>What one game gets: algorithm, preset, video card (DXGI index + ids; -1 = Magpie decides) and automatic start.</summary>
public sealed record MagpieGameProfile(string ExePath, string WindowClass, string Name, UpscaleAlgorithm Algorithm, UpscalePreset Preset, int GpuIndex, uint VendorId, uint DeviceId, bool AutoScale)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => $"{Algorithm.Ru()} · {Preset switch { UpscalePreset.Quality => "качество", UpscalePreset.Balance => "баланс", _ => "производительность" }}" +
                             (AutoScale ? " · автоматически" : string.Empty) + $" · окно {WindowClass}";
}

/// <summary>
/// Magpie (GPLv3, github.com/Blinue/Magpie) as a separate program. FrameLab keeps its own portable copy in its data folder — downloaded from
/// Magpie's official update channel on request, never bundled — and only edits that copy's config.json. A Magpie the user installed
/// themselves is never touched. FrameLab contains no Magpie code: it writes the documented JSON config and starts the exe.
/// </summary>
public static class MagpieSetup
{
    public const string VersionUrl = "https://raw.githubusercontent.com/Blinue/Magpie/main/version.json";
    public const string ProjectUrl = "https://github.com/Blinue/Magpie";

    /// <summary>Name prefix of the scaling modes FrameLab manages; modes the user made in Magpie keep their place after ours.</summary>
    public const string ModePrefix = "FrameLab · ";

    /// <summary>Magpie's own default shortcuts (Alt+Shift+A — fullscreen, Alt+Shift+Q — windowed).</summary>
    public const string ScaleHotkey = "Alt+Shift+A";

    public static string InstallDir => Path.Combine(AppPaths.Root, "tools", "Magpie");
    public static string Exe => Path.Combine(InstallDir, "Magpie.exe");
    public static string ConfigPath => Path.Combine(InstallDir, "config", "config.json");
    public static bool Installed => File.Exists(Exe);

    public static readonly UpscaleAlgorithm[] Algorithms = Enum.GetValues<UpscaleAlgorithm>();

    public static string Ru(this UpscaleAlgorithm a) => a switch
    {
        UpscaleAlgorithm.Fsr => "AMD FSR 1",
        UpscaleAlgorithm.Nis => "NVIDIA NIS",
        UpscaleAlgorithm.Cas => "AMD CAS",
        UpscaleAlgorithm.Lanczos => "Lanczos",
        UpscaleAlgorithm.Integer => "Целочисленное ×2",
        _ => "Anime4K",
    };

    public static string Describe(this UpscaleAlgorithm a) => a switch
    {
        UpscaleAlgorithm.Fsr => "Лучший универсальный вариант для 3D-игр: чёткие края, мало артефактов, лёгкий для видеокарты.",
        UpscaleAlgorithm.Nis => "Похож на FSR, чуть мягче; на некоторых сценах меньше ореолов.",
        UpscaleAlgorithm.Cas => "Очень лёгкий: масштаб плюс резкость. Для слабой и встроенной графики.",
        UpscaleAlgorithm.Lanczos => "Классическое масштабирование без «додумывания» деталей. Самый честный к исходной картинке.",
        UpscaleAlgorithm.Integer => "Каждый пиксель ровно ×2 без размытия — для пиксельных и старых 2D-игр (окно ровно в половину экрана).",
        _ => "Для аниме и рисованной графики. Тяжёлый: на слабой карте сам съест FPS.",
    };

    /// <summary>Share of the screen resolution to run the game at; the rest is upscaled.</summary>
    public static double RenderScale(UpscalePreset p) => p switch
    {
        UpscalePreset.Quality => 1 / 1.3,
        UpscalePreset.Balance => 1 / 1.5,
        _ => 0.5,
    };

    /// <summary>Lower render resolution needs a bit more sharpening.</summary>
    public static double Sharpness(UpscaleAlgorithm a, UpscalePreset p) => (a, p) switch
    {
        (UpscaleAlgorithm.Fsr, UpscalePreset.Quality) => 0.87,
        (UpscaleAlgorithm.Fsr, UpscalePreset.Balance) => 0.8,
        (UpscaleAlgorithm.Fsr, _) => 0.7,
        (UpscaleAlgorithm.Nis, UpscalePreset.Quality) => 0.5,
        (UpscaleAlgorithm.Nis, UpscalePreset.Balance) => 0.6,
        (UpscaleAlgorithm.Nis, _) => 0.7,
        (_, UpscalePreset.Quality) => 0.4,
        (_, UpscalePreset.Balance) => 0.5,
        _ => 0.6,
    };

    /// <summary>The game's window size to choose for a preset on this screen (even numbers, at least 640×360).</summary>
    public static (int Width, int Height) GameResolution(int screenW, int screenH, UpscalePreset p, UpscaleAlgorithm a)
    {
        if (a == UpscaleAlgorithm.Integer)
        {
            return (screenW / 2, screenH / 2);
        }

        var s = RenderScale(p);
        int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
        return (Math.Max(640, Even(screenW * s)), Math.Max(360, Even(screenH * s)));
    }

    public static async Task<MagpieRelease> LatestAsync(HttpClient http, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(VersionUrl, ct));
        var root = doc.RootElement;
        var x64 = root.GetProperty("binary").GetProperty(RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "ARM64" : "x64");
        return new MagpieRelease(root.GetProperty("version").GetString() ?? "?", x64.GetProperty("url").GetString() ?? string.Empty, x64.GetProperty("hash").GetString() ?? string.Empty);
    }

    /// <summary>Downloads the release zip, checks its MD5 against version.json and unpacks it into FrameLab's copy.</summary>
    public static async Task InstallAsync(HttpClient http, MagpieRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        var zip = Path.Combine(AppPaths.Root, "downloads", $"Magpie-v{release.Version}.zip");
        await GitHubReleases.DownloadAsync(http, new ReleaseAsset(release.Version, Path.GetFileName(zip), release.Url, 0, ProjectUrl), zip, progress, ct);
        var md5 = Convert.ToHexString(MD5.HashData(await File.ReadAllBytesAsync(zip, ct)));
        if (!md5.Equals(release.Md5, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zip);
            throw new InvalidDataException($"Контрольная сумма архива Magpie не совпала ({md5} ≠ {release.Md5}). Архив удалён, ничего не установлено.");
        }

        Stop();
        var staging = InstallDir + ".new";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        ZipFile.ExtractToDirectory(zip, staging); // rejects entries that point outside the folder
        var exe = Directory.EnumerateFiles(staging, "Magpie.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidDataException("В архиве нет Magpie.exe.");
        var payload = Path.GetDirectoryName(exe)!;

        // Keep the user's config across updates.
        var oldConfig = File.Exists(ConfigPath) ? await File.ReadAllTextAsync(ConfigPath, ct) : null;
        if (Directory.Exists(InstallDir))
        {
            Directory.Delete(InstallDir, recursive: true);
        }

        Directory.Move(payload, InstallDir);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        // An existing config file (even empty) is what makes Magpie run in portable mode, away from any Magpie the user installed.
        await File.WriteAllTextAsync(ConfigPath, oldConfig ?? string.Empty, ct);
        File.WriteAllText(Path.Combine(InstallDir, "FrameLab-version.txt"), release.Version);
        Log.Info($"Magpie {release.Version} установлен в {InstallDir}");
    }

    public static string? InstalledVersion()
    {
        var f = Path.Combine(InstallDir, "FrameLab-version.txt");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
    }

    public static void Uninstall()
    {
        Stop();
        if (Directory.Exists(InstallDir))
        {
            Directory.Delete(InstallDir, recursive: true);
        }
    }

    public static bool Running() => OurProcesses().Any();

    /// <summary>Starts FrameLab's copy in the tray (-t). Working folder = its own, so the portable config is the one used.</summary>
    public static void Start()
    {
        if (!Installed || Running())
        {
            return;
        }

        Process.Start(new ProcessStartInfo(Exe, "-t") { UseShellExecute = true, WorkingDirectory = InstallDir })?.Dispose();
    }

    /// <summary>Ends FrameLab's copy only (matched by its path). Magpie writes its config on exit, so it must be closed before FrameLab edits it.</summary>
    public static void Stop()
    {
        foreach (var p in OurProcesses())
        {
            using (p)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log.Warn($"Не удалось закрыть Magpie: {ex.Message}");
                }
            }
        }
    }

    private static IEnumerable<Process> OurProcesses()
    {
        foreach (var p in Process.GetProcessesByName("Magpie"))
        {
            string? path = null;
            try
            {
                path = p.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }

            if (path is not null && path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
            {
                yield return p;
            }
            else
            {
                p.Dispose();
            }
        }
    }

    public static void WriteConfig(string path, UpscaleAlgorithm defaultAlgorithm, UpscalePreset defaultPreset, IReadOnlyList<MagpieGameProfile> games)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        File.WriteAllText(path, BuildConfig(text, defaultAlgorithm, defaultPreset, games), new UTF8Encoding(false));
    }

    /// <summary>
    /// Merges FrameLab's settings into a Magpie config: our scaling modes go first (one per algorithm × preset), modes the user made
    /// are kept after them with their profiles re-pointed, the default profile and our game profiles get the chosen mode.
    /// Everything else in the file is left as Magpie wrote it.
    /// </summary>
    public static string BuildConfig(string existing, UpscaleAlgorithm defaultAlgorithm, UpscalePreset defaultPreset, IReadOnlyList<MagpieGameProfile> games)
    {
        var root = string.IsNullOrWhiteSpace(existing) ? new JsonObject() : JsonNode.Parse(existing) as JsonObject ?? new JsonObject();

        // FrameLab updates Magpie itself, through the official channel, only on request.
        root["autoCheckForUpdates"] = false;

        var oldModes = root["scalingModes"] as JsonArray ?? new JsonArray();
        var userModes = oldModes.Select((m, i) => (Node: m, Index: i))
            .Where(x => !(x.Node?["name"]?.GetValue<string>() ?? string.Empty).StartsWith(ModePrefix, StringComparison.Ordinal))
            .ToList();
        var ours = Algorithms.SelectMany(a => Enum.GetValues<UpscalePreset>().Select(p => Mode(a, p))).ToList();
        var modes = new JsonArray();
        foreach (var m in ours)
        {
            modes.Add(m);
        }

        var remap = new Dictionary<int, int>();
        foreach (var (node, index) in userModes)
        {
            remap[index] = modes.Count;
            modes.Add(node?.DeepClone());
        }

        root["scalingModes"] = modes;

        var profiles = root["profiles"] as JsonArray ?? new JsonArray();
        var list = profiles.Select(p => p?.DeepClone() as JsonObject ?? new JsonObject()).ToList();
        if (list.Count == 0)
        {
            list.Add(new JsonObject());
        }

        // Existing profiles that used a user mode follow it to its new index; ones that used one of ours get ours again below or fall back.
        foreach (var p in list)
        {
            var idx = p["scalingMode"]?.GetValue<int>() ?? -1;
            p["scalingMode"] = remap.TryGetValue(idx, out var n) ? n : idx < oldModes.Count && idx >= 0 ? IndexOf(defaultAlgorithm, defaultPreset) : -1;
        }

        list[0]["scalingMode"] = IndexOf(defaultAlgorithm, defaultPreset);
        list[0]["captureMethod"] = 0; // Windows.Graphics.Capture

        foreach (var g in games)
        {
            var p = list.Skip(1).FirstOrDefault(x => string.Equals(x["pathRule"]?.GetValue<string>(), g.ExePath, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                p = new JsonObject();
                list.Add(p);
            }

            p["name"] = g.Name;
            p["packaged"] = false;
            p["pathRule"] = g.ExePath;
            p["classNameRule"] = g.WindowClass;
            p["launcherPath"] ??= string.Empty;
            p["autoScale"] = g.AutoScale ? 1 : 0;
            p["launchParameters"] ??= string.Empty;
            p["scalingMode"] = IndexOf(g.Algorithm, g.Preset);
            p["captureMethod"] = 0;
            p["graphicsCardId"] = new JsonObject { ["idx"] = g.GpuIndex, ["vendorId"] = g.VendorId, ["deviceId"] = g.DeviceId };
        }

        var arr = new JsonArray();
        foreach (var p in list)
        {
            arr.Add(p);
        }

        root["profiles"] = arr;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static int IndexOf(UpscaleAlgorithm a, UpscalePreset p) => (int)a * 3 + (int)p;

    private static JsonObject Mode(UpscaleAlgorithm a, UpscalePreset p)
    {
        var sharp = Math.Round(Sharpness(a, p), 2);
        var presetName = p switch { UpscalePreset.Quality => "качество", UpscalePreset.Balance => "баланс", _ => "производительность" };
        JsonObject Fit(string name, double? sharpness = null)
        {
            var e = new JsonObject { ["name"] = name, ["scalingType"] = 1, ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 } };
            if (sharpness is { } s)
            {
                e["parameters"] = new JsonObject { ["sharpness"] = s };
            }

            return e;
        }

        var effects = a switch
        {
            UpscaleAlgorithm.Fsr => new JsonArray(Fit("FSR\\FSR_EASU"), new JsonObject { ["name"] = "FSR\\FSR_RCAS", ["parameters"] = new JsonObject { ["sharpness"] = sharp } }),
            UpscaleAlgorithm.Nis => new JsonArray(Fit("NIS\\NIS", sharp)),
            UpscaleAlgorithm.Cas => new JsonArray(Fit("CAS\\CAS_Scaling", sharp)),
            UpscaleAlgorithm.Lanczos => new JsonArray(Fit("Lanczos")),
            UpscaleAlgorithm.Integer => new JsonArray(new JsonObject { ["name"] = "Nearest", ["scalingType"] = 0, ["scale"] = new JsonObject { ["x"] = 2.0, ["y"] = 2.0 } }),
            _ => new JsonArray(new JsonObject { ["name"] = p == UpscalePreset.Performance ? "Anime4K\\Anime4K_Upscale_S" : "Anime4K\\Anime4K_Upscale_Denoise_L" }, Fit("Lanczos")),
        };
        return new JsonObject { ["name"] = $"{ModePrefix}{a.Ru()} ({presetName})", ["effects"] = effects };
    }
}

/// <summary>The class name of a running game's main window — Magpie matches profiles by exe path and window class. Reads the window list only.</summary>
public static class GameWindows
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    /// <summary>Class of the first visible top-level window of a process with this name, or null if the game isn't running.</summary>
    public static string? ClassOf(string processName)
    {
        var pids = Process.GetProcessesByName(processName).Select(p =>
        {
            using (p)
            {
                return (uint)p.Id;
            }
        }).ToHashSet();
        if (pids.Count == 0)
        {
            return null;
        }

        string? found = null;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid) || !IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero)
            {
                return true; // not ours, hidden, or an owned popup
            }

            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            found = sb.ToString();
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
