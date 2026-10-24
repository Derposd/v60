using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.GtaRp;

/// <summary>Read-only search for GTA V, its settings file and the RAGE MP client.</summary>
public sealed class GtaLocator
{
    public const string SteamAppId = "271590";

    private const string RockstarKey = @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V";
    private const string RockstarShortKey = @"SOFTWARE\WOW6432Node\Rockstar Games\GTAV";
    private static readonly char[] DriveLetters = { 'C', 'D', 'E', 'F', 'G', 'H' };

    private readonly IFileSystem _fs;
    private readonly IRegistry _reg;
    private readonly string _documents;

    public GtaLocator(IFileSystem fs, IRegistry reg, string documentsDir)
    {
        _fs = fs;
        _reg = reg;
        _documents = documentsDir;
    }

    public GtaInfo Locate(Func<string, bool>? isProcessRunning = null)
    {
        isProcessRunning ??= _ => false;
        var (dir, edition) = FindGame();
        var settings = Path.Combine(_documents, "Rockstar Games", "GTA V", "settings.xml");
        return new GtaInfo
        {
            GameDir = dir,
            Edition = edition,
            SettingsPath = settings,
            SettingsExists = _fs.FileExists(settings),
            RageMpDir = FindRageMp(),
            GameRunning = isProcessRunning("GTA5"),
            RageMpRunning = isProcessRunning("ragemp_v"),
        };
    }

    private (string? Dir, GtaEdition Edition) FindGame()
    {
        // 1. What the launchers wrote into the registry.
        foreach (var (key, name, edition) in new[]
                 {
                     (RockstarKey, "InstallFolder", GtaEdition.Rockstar),
                     (RockstarShortKey, "InstallFolderSteam", GtaEdition.Steam),
                     (RockstarShortKey, "InstallFolderEpic", GtaEdition.Epic),
                 })
        {
            var value = _reg.GetValue(RegHive.LocalMachine, key, name)?.Data;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var found = ValidGameDir(value);
            if (found is not null)
            {
                return (found, edition);
            }
        }

        // 2. Steam libraries.
        try
        {
            var steam = new SteamLocator(_fs, _reg);
            var steamPath = steam.FindSteamPath();
            if (steamPath is not null)
            {
                foreach (var lib in steam.ReadLibraries(steamPath))
                {
                    var found = ValidGameDir(Path.Combine(lib, "steamapps", "common", "Grand Theft Auto V"));
                    if (found is not null)
                    {
                        return (found, GtaEdition.Steam);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Vdf.VdfParseException)
        {
            Log.Warn($"GTA V: не удалось прочитать библиотеки Steam: {ex.Message}");
        }

        // 3. Usual folders on every fixed-drive letter.
        foreach (var letter in DriveLetters)
        {
            foreach (var (sub, edition) in new[]
                     {
                         (@"Program Files\Rockstar Games\Grand Theft Auto V", GtaEdition.Rockstar),
                         (@"Program Files (x86)\Rockstar Games\Grand Theft Auto V", GtaEdition.Rockstar),
                         (@"Rockstar Games\Grand Theft Auto V", GtaEdition.Rockstar),
                         (@"Program Files\Epic Games\GTAV", GtaEdition.Epic),
                         (@"Games\Grand Theft Auto V", GtaEdition.Unknown),
                         (@"Grand Theft Auto V", GtaEdition.Unknown),
                         (@"GTAV", GtaEdition.Unknown),
                         (@"GTA V", GtaEdition.Unknown),
                     })
            {
                var found = ValidGameDir($@"{letter}:\{sub}");
                if (found is not null)
                {
                    return (found, edition);
                }
            }
        }

        return (null, GtaEdition.Unknown);
    }

    /// <summary>The folder (or its parent: some launchers store a sub-folder) that really contains GTA5.exe.</summary>
    private string? ValidGameDir(string path)
    {
        var trimmed = path.Trim().Trim('"').TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (_fs.FileExists(Path.Combine(trimmed, GtaInfo.GameExeName)))
        {
            return trimmed;
        }

        var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        if (cut <= 0)
        {
            return null;
        }

        var parent = trimmed[..cut];
        return _fs.FileExists(Path.Combine(parent, GtaInfo.GameExeName)) ? parent : null;
    }

    private string? FindRageMp()
    {
        foreach (var letter in DriveLetters)
        {
            var dir = $@"{letter}:\RAGEMP";
            if (_fs.FileExists(Path.Combine(dir, GtaInfo.RageMpExeName)) || _fs.FileExists(Path.Combine(dir, "updater.exe")) || _fs.DirectoryExists(Path.Combine(dir, "client_resources")))
            {
                return dir;
            }
        }

        return null;
    }
}
