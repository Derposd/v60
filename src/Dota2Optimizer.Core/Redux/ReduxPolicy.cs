using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.Redux;

public enum ReduxVerdict
{
    /// <summary>A data file the game reads. Copied.</summary>
    Install,

    /// <summary>Code or an unsafe path. Never copied, whatever the pack says.</summary>
    Blocked,

    /// <summary>Not copied: documentation, previews, files outside the pack root, or types the game does not use.</summary>
    Skipped,
}

public sealed record ReduxVerdictInfo(ReduxVerdict Verdict, string? Reason = null);

/// <summary>
/// What may and may not be put into the GTA V folder. A graphics pack is data — models, textures, timecycle, meta files. Anything that runs
/// code (ASI plugins, DLLs such as ScriptHook or ReShade, scripts, executables) is refused: it is the usual carrier of malware, and the
/// anti-cheat of roleplay projects bans for it. Unknown file types are skipped rather than guessed at.
/// </summary>
public static partial class ReduxPolicy
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".asi", ".sys", ".ocx", ".cpl", ".scr", ".com", ".pif", ".msi", ".appx", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe",
        ".js", ".jse", ".wsf", ".hta", ".lnk", ".reg", ".jar", ".py", ".lua", ".cs", ".sh", ".ysc", ".sc",
    };

    // Data formats the game itself reads (RAGE resources, archives, configuration).
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rpf", ".ytd", ".ydr", ".yft", ".ydd", ".ymap", ".ytyp", ".ymt", ".ybn", ".ycd", ".ynv", ".ynd", ".yld", ".ypt", ".yed", ".yvr", ".ywr",
        ".awc", ".rel", ".dat", ".xml", ".meta", ".cfg", ".gfx", ".nametable", ".dds", ".ini", ".txt",
    };

    // Files at the top of the pack that are documentation, not game data.
    private static readonly HashSet<string> TopLevelJunk = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".url", ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".html", ".htm", ".doc", ".docx", ".nfo", ".diz",
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Words that point to object removal or see-through textures — an advantage mod, not a graphics mod.</summary>
    [GeneratedRegex(@"x-?ray|wall-?hack|transparen|no-?props|no-?bush|no-?tree|remove[ds]?[_\- ]|прозрач", RegexOptions.IgnoreCase)]
    private static partial Regex AdvantageWords();

    /// <summary>The path is relative, has no "..", no drive or stream marker, no reserved Windows device names.</summary>
    public static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0 || segment.Any(char.IsControl))
            {
                return false;
            }

            var stem = segment.Split('.')[0];
            if (ReservedNames.Contains(stem))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Classifies a path that is already relative to the GTA V folder.</summary>
    public static ReduxVerdictInfo Classify(string gtaRelativePath, bool atPackTop)
    {
        if (!IsSafeRelative(gtaRelativePath))
        {
            return new(ReduxVerdict.Blocked, "небезопасный путь");
        }

        var ext = Path.GetExtension(gtaRelativePath);
        if (BlockedExtensions.Contains(ext))
        {
            return new(ReduxVerdict.Blocked, ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(".asi", StringComparison.OrdinalIgnoreCase)
                ? "исполняемый код (ASI / DLL — ScriptHook, ReShade, ENB и подобные)"
                : "исполняемый файл или скрипт");
        }

        var name = Path.GetFileName(gtaRelativePath);
        if (name.Equals("commandline.txt", StringComparison.OrdinalIgnoreCase))
        {
            return new(ReduxVerdict.Blocked, "commandline.txt меняет параметры запуска игры");
        }

        if (atPackTop && TopLevelJunk.Contains(ext))
        {
            return new(ReduxVerdict.Skipped, "описание или картинка");
        }

        return AllowedExtensions.Contains(ext)
            ? new(ReduxVerdict.Install)
            : new(ReduxVerdict.Skipped, $"тип «{(ext.Length == 0 ? "без расширения" : ext)}» игра не читает или он неизвестен");
    }

    public static bool LooksLikeAdvantageMod(string text) => AdvantageWords().IsMatch(text);
}
