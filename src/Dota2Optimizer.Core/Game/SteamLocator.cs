using System.Globalization;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Vdf;

namespace Dota2Optimizer.Core.Game;

/// <summary>Finds Steam, its libraries, the Dota 2 install and per-account config folders. Read-only.</summary>
public sealed class SteamLocator
{
    public const string DotaAppId = "570";
    private const long SteamId64Base = 76561197960265728;

    private readonly IFileSystem _fs;
    private readonly IRegistry _reg;

    public SteamLocator(IFileSystem fs, IRegistry reg)
    {
        _fs = fs;
        _reg = reg;
    }

    public SteamInfo Locate(Func<string, bool>? isProcessRunning = null)
    {
        isProcessRunning ??= _ => false;
        var steamPath = FindSteamPath();
        if (steamPath is null)
        {
            return new SteamInfo { SteamRunning = isProcessRunning("steam") };
        }

        var libraries = ReadLibraries(steamPath);
        string? installDir = null;
        string? buildId = null;
        foreach (var lib in libraries)
        {
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{DotaAppId}.acf");
            if (!_fs.FileExists(manifest))
            {
                continue;
            }

            try
            {
                var app = VdfParser.Parse(_fs.ReadAllText(manifest));
                var dir = app.ValueOf("installdir") ?? "dota 2 beta";
                installDir = Path.Combine(lib, "steamapps", "common", dir);
                buildId = app.ValueOf("buildid");
                break;
            }
            catch (VdfParseException ex)
            {
                Log.Warn($"Не удалось прочитать {manifest}: {ex.Message}");
            }
        }

        var accountId = FindActiveAccount(steamPath);
        var userdata = Path.Combine(steamPath, "userdata");
        var cloudDirs = _fs.EnumerateDirectories(userdata)
            .Select(acc => Path.Combine(acc, DotaAppId, "remote", "cfg"))
            .Where(_fs.DirectoryExists)
            .ToList();

        string? cfgDir = installDir is null ? null : Path.Combine(installDir, "game", "dota", "cfg");
        var videoPaths = new List<string>();
        if (cfgDir is not null && _fs.FileExists(Path.Combine(cfgDir, "video.txt")))
        {
            videoPaths.Add(Path.Combine(cfgDir, "video.txt"));
        }

        if (accountId is not null)
        {
            foreach (var sub in new[] { "remote", "local" })
            {
                var p = Path.Combine(userdata, accountId, DotaAppId, sub, "cfg", "video.txt");
                if (_fs.FileExists(p))
                {
                    videoPaths.Add(p);
                }
            }
        }

        var localConfig = accountId is null ? null : Path.Combine(userdata, accountId, "config", "localconfig.vdf");
        var exe = installDir is null ? null : Path.Combine(installDir, "game", "bin", "win64", "dota2.exe");

        return new SteamInfo
        {
            SteamPath = steamPath,
            Libraries = libraries,
            DotaInstallDir = installDir,
            DotaExe = exe,
            DotaCfgDir = cfgDir,
            VideoConfigPaths = videoPaths,
            CloudCfgDirs = cloudDirs,
            ActiveAccountId = accountId,
            LocalConfigPath = localConfig is not null && _fs.FileExists(localConfig) ? localConfig : null,
            SteamRunning = isProcessRunning("steam"),
            DotaRunning = isProcessRunning("dota2"),
            DotaBuildId = buildId,
        };
    }

    public string? FindSteamPath()
    {
        var candidates = new[]
        {
            _reg.GetValue(RegHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Data,
            _reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")?.Data,
            _reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")?.Data,
        };
        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('/', '\\'))
            .FirstOrDefault(p => _fs.DirectoryExists(p));
    }

    public IReadOnlyList<string> ReadLibraries(string steamPath)
    {
        var result = new List<string> { steamPath };
        var file = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!_fs.FileExists(file))
        {
            return result;
        }

        try
        {
            var root = VdfParser.Parse(_fs.ReadAllText(file));
            foreach (var entry in root.Children.Where(c => int.TryParse(c.Key, out _)))
            {
                // New format: "0" { "path" "..." }, old format: "1" "D:\\Games\\Steam".
                var path = entry.IsObject ? entry.ValueOf("path") : entry.Value;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    path = path.Replace('/', '\\');
                    if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(path);
                    }
                }
            }
        }
        catch (VdfParseException ex)
        {
            Log.Warn($"libraryfolders.vdf: {ex.Message}");
        }

        return result;
    }

    /// <summary>Steam account id (the folder name under userdata) of the most recently logged-in user.</summary>
    public string? FindActiveAccount(string steamPath)
    {
        var active = _reg.GetValue(RegHive.CurrentUser, @"Software\Valve\Steam\ActiveProcess", "ActiveUser")?.AsInt();
        if (active is > 0)
        {
            return active.Value.ToString(CultureInfo.InvariantCulture);
        }

        var loginUsers = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (_fs.FileExists(loginUsers))
        {
            try
            {
                var users = VdfParser.Parse(_fs.ReadAllText(loginUsers));
                var recent = users.Children.FirstOrDefault(u => u.ValueOf("MostRecent") == "1") ?? users.Children.FirstOrDefault();
                if (recent is not null && long.TryParse(recent.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id64))
                {
                    return (id64 - SteamId64Base).ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (VdfParseException ex)
            {
                Log.Warn($"loginusers.vdf: {ex.Message}");
            }
        }

        var accounts = _fs.EnumerateDirectories(Path.Combine(steamPath, "userdata"))
            .Select(PathEx.FileName)
            .Where(n => n is not null && n != "0" && n.All(char.IsDigit))
            .ToList();
        return accounts.Count == 1 ? accounts[0] : null;
    }
}
