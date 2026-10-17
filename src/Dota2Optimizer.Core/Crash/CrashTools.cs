using System.Diagnostics;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Crash;

public sealed record DumpFile(string Path, DateTime Time, long SizeBytes, string Kind);

public sealed record CacheLocation(string Name, string Path, string Note)
{
    public long SizeBytes { get; init; }
    public bool Exists { get; init; }
}

public sealed record CacheCleanResult(long FreedBytes, int Deleted, int Skipped);

public sealed record RuntimeCheck(string Name, bool Ok, string Details, string? Link);

public sealed record Conflict(string Name, string Advice);

/// <summary>Crash-related helpers: dumps, logs, caches, runtimes. Only cache cleaning writes anything, and only after confirmation in the UI.</summary>
public static class CrashTools
{
    public const string VcRedistLink = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string LocalLow => Path.Combine(Path.GetDirectoryName(LocalAppData) ?? LocalAppData, "LocalLow");

    public static IReadOnlyList<DumpFile> FindDumps(SteamInfo steam)
    {
        var places = new List<(string Dir, string Pattern, string Kind, bool Recursive)>
        {
            (Path.Combine(LocalAppData, "CrashDumps"), "dota2*.dmp", "Дамп Windows (WER)", false),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump"), "*.dmp", "Синий экран (BSOD)", false),
        };
        if (steam.DotaInstallDir is not null)
        {
            places.Add((Path.Combine(steam.DotaInstallDir, "game"), "*.mdmp", "Дамп Dota 2", true));
        }

        if (steam.SteamPath is not null)
        {
            places.Add((Path.Combine(steam.SteamPath, "dumps"), "*.dmp", "Дамп Steam", false));
        }

        var result = new List<DumpFile>();
        foreach (var (dir, pattern, kind, recursive) in places)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var f in Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, MaxRecursionDepth = 4 }))
                {
                    var info = new FileInfo(f);
                    result.Add(new DumpFile(f, info.LastWriteTime, info.Length, kind));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Minidump folder may need admin to list; that's fine.
            }
        }

        return result.OrderByDescending(d => d.Time).Take(50).ToList();
    }

    public static IReadOnlyList<string> LogFiles(SteamInfo steam)
    {
        var list = new List<string>();
        if (steam.DotaInstallDir is not null)
        {
            list.Add(Path.Combine(steam.DotaInstallDir, "game", "dota", "console.log"));
        }

        if (steam.SteamPath is not null)
        {
            list.Add(Path.Combine(steam.SteamPath, "logs", "content_log.txt"));
            list.Add(Path.Combine(steam.SteamPath, "logs", "connection_log.txt"));
        }

        return list.Where(File.Exists).ToList();
    }

    /// <summary>Reads at most the last <paramref name="maxBytes"/> of a log that Steam/Dota may still have open.</summary>
    public static string ReadTail(string path, int maxBytes = 2 * 1024 * 1024)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > maxBytes)
        {
            fs.Seek(-maxBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<CacheLocation> ShaderCaches(SteamInfo steam, IEnumerable<GpuVendor> vendors)
    {
        var v = vendors.ToHashSet();
        var list = new List<CacheLocation>();
        foreach (var lib in steam.Libraries)
        {
            list.Add(new CacheLocation("Кеш шейдеров Steam для Dota 2", Path.Combine(lib, "steamapps", "shadercache", "570"),
                "Предкомпилированные шейдеры Steam (в основном Vulkan). Steam скачает/пересоберёт их заново."));
        }

        list.Add(new CacheLocation("Кеш шейдеров DirectX", Path.Combine(LocalAppData, "D3DSCache"), "Общий кеш DirectX. Пересоздаётся автоматически."));
        if (v.Contains(GpuVendor.Nvidia))
        {
            list.Add(new CacheLocation("NVIDIA DXCache", Path.Combine(LocalLow, "NVIDIA", "PerDriverVersion", "DXCache"), "Кеш шейдеров драйвера NVIDIA."));
            list.Add(new CacheLocation("NVIDIA GLCache (Vulkan/OpenGL)", Path.Combine(LocalLow, "NVIDIA", "PerDriverVersion", "GLCache"), "Кеш драйвера NVIDIA для Vulkan/OpenGL."));
            list.Add(new CacheLocation("NVIDIA DXCache (старый путь)", Path.Combine(LocalAppData, "NVIDIA", "DXCache"), "Используется старыми драйверами."));
            list.Add(new CacheLocation("NVIDIA GLCache (старый путь)", Path.Combine(LocalAppData, "NVIDIA", "GLCache"), "Используется старыми драйверами."));
        }

        if (v.Contains(GpuVendor.Amd))
        {
            list.Add(new CacheLocation("AMD DxCache", Path.Combine(LocalAppData, "AMD", "DxCache"), "Кеш шейдеров DirectX драйвера AMD."));
            list.Add(new CacheLocation("AMD DxcCache", Path.Combine(LocalAppData, "AMD", "DxcCache"), "Кеш компилятора шейдеров AMD."));
            list.Add(new CacheLocation("AMD VkCache", Path.Combine(LocalAppData, "AMD", "VkCache"), "Кеш шейдеров Vulkan драйвера AMD."));
        }

        if (v.Contains(GpuVendor.Intel))
        {
            list.Add(new CacheLocation("Intel ShaderCache", Path.Combine(LocalLow, "Intel", "ShaderCache"), "Кеш шейдеров драйвера Intel."));
        }

        return list.Select(c => c with { Exists = Directory.Exists(c.Path), SizeBytes = DirSize(c.Path) }).ToList();
    }

    /// <summary>
    /// Deletes cache files. Not backed up on purpose: caches are regenerated by the game/driver,
    /// and a backup would defeat the purpose. Locked files are skipped.
    /// </summary>
    public static CacheCleanResult Clean(IEnumerable<CacheLocation> caches)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var c in caches.Where(c => Directory.Exists(c.Path)))
        {
            foreach (var f in Directory.EnumerateFiles(c.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                try
                {
                    var len = new FileInfo(f).Length;
                    File.Delete(f);
                    freed += len;
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }

            Log.Info($"Очищен кеш «{c.Name}» ({c.Path})");
        }

        return new CacheCleanResult(freed, deleted, skipped);
    }

    public static long DirSize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Sum(f =>
                {
                    try
                    {
                        return new FileInfo(f).Length;
                    }
                    catch (IOException)
                    {
                        return 0L;
                    }
                });
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static IReadOnlyList<RuntimeCheck> CheckRuntimes(IRegistry reg)
    {
        var list = new List<RuntimeCheck>();
        var vc = reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "Installed")?.AsInt() == 1
                 || reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "Installed")?.AsInt() == 1;
        var vcVersion = reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "Version")?.Data
                        ?? reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "Version")?.Data;
        list.Add(new RuntimeCheck("Visual C++ 2015–2022 (x64)", vc,
            vc ? $"Установлен {vcVersion}" : "Не найден. Без него игра и Steam могут не запускаться или вылетать.", vc ? null : VcRedistLink));

        var sys = Environment.SystemDirectory;
        foreach (var (dll, name, missing) in new[]
        {
            ("d3d11.dll", "DirectX 11", "Отсутствует системный DirectX 11 — система повреждена, запустите sfc /scannow."),
            ("dxgi.dll", "DXGI", "Отсутствует DXGI — система повреждена, запустите sfc /scannow."),
            ("d3dcompiler_47.dll", "Компилятор шейдеров D3D", "Нет d3dcompiler_47.dll — установите обновления Windows."),
            ("vulkan-1.dll", "Vulkan Runtime", "Нет загрузчика Vulkan — он ставится вместе с драйвером видеокарты. Без него -vulkan не работает; переустановите драйвер."),
        })
        {
            var ok = File.Exists(Path.Combine(sys, dll));
            list.Add(new RuntimeCheck(name, ok, ok ? "Найден" : missing, null));
        }

        return list;
    }

    public static IReadOnlyList<Conflict> KnownConflicts { get; } = new[]
    {
        new Conflict("Оверлей Discord", "Discord → Настройки → Игровой оверлей → отключить для Dota 2. Также можно выключить аппаратное ускорение Discord."),
        new Conflict("MSI Afterburner / RivaTuner", "Оверлей RTSS иногда вызывает вылеты на Vulkan. Разгон из Afterburner — частая причина TDR. Проверьте игру с закрытым Afterburner и сброшенным профилем."),
        new Conflict("NVIDIA App / GeForce Experience", "Оверлей и «Мгновенный повтор» пишут видео в фоне. Отключите повтор, если не пользуетесь; при вылетах — выключите оверлей."),
        new Conflict("Оверлей AMD (Radeon Software)", "Alt+R оверлей и запись: отключите Instant Replay, если не нужен; при вылетах — выключите оверлей метрик."),
        new Conflict("Оверлей Steam", "Обычно безопасен. При вылетах в модуле gameoverlayrenderer — отключите для Dota 2 в свойствах игры."),
        new Conflict("Антивирус", "Добавьте папку игры (steamapps\\common\\dota 2 beta) в исключения сканирования — так он не будет проверять файлы при каждой загрузке. Windows Defender не отключайте: достаточно исключения папки. Приложение само антивирус не настраивает."),
        new Conflict("Программы-«ускорители» и чистильщики", "Утилиты, которые «освобождают RAM» или меняют реестр, часто вызывают фризы и вылеты. Удалите их."),
    };

    /// <summary>Asks Steam to verify Dota 2 files. Steam shows its own progress window.</summary>
    public static void StartValidate() => OpenUri("steam://validate/570");

    /// <summary>Steam's official "Clear download cache" (asks to log in again).</summary>
    public static void FlushSteamDownloadCache() => OpenUri("steam://flushconfig");

    public static void OpenUri(string uri)
    {
        Log.Info($"Открываю {uri}");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
    }
}
