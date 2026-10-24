using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.Redux;

public enum ReduxRootKind
{
    /// <summary>A folder that is clearly the GTA V folder layout (mods, update, x64…rpf) was found.</summary>
    Detected,

    /// <summary>The pack is wrapped in a single folder; its contents were taken as the GTA V layout.</summary>
    Wrapper,

    /// <summary>Could not tell where the pack starts. Installing needs the player's explicit OK.</summary>
    Unknown,
}

public sealed record ReduxFilePlan(string PackPath, string GtaPath, long Length, ReduxVerdict Verdict, string? Reason);

/// <summary>What installing a pack would do, worked out before anything is copied.</summary>
public sealed class ReduxPlan
{
    public required string Name { get; init; }
    public required string PayloadPrefix { get; init; }
    public required ReduxRootKind RootKind { get; init; }
    public required IReadOnlyList<ReduxFilePlan> Files { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IEnumerable<ReduxFilePlan> ToInstall => Files.Where(f => f.Verdict == ReduxVerdict.Install);
    public IEnumerable<ReduxFilePlan> Blocked => Files.Where(f => f.Verdict == ReduxVerdict.Blocked);
    public IEnumerable<ReduxFilePlan> Skipped => Files.Where(f => f.Verdict == ReduxVerdict.Skipped);
    public int InstallCount => ToInstall.Count();
    public long InstallBytes => ToInstall.Sum(f => f.Length);
    public bool CanInstall => InstallCount > 0;
    public bool NeedsRootConfirmation => RootKind == ReduxRootKind.Unknown;
}

/// <summary>Works out where a pack's files go inside the GTA V folder and what the policy says about each of them.</summary>
public static class ReduxAnalyzer
{
    private static readonly Regex RootArchive = new(@"^(x64[a-z]|common|update|platform|patch)\.rpf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> RootFolders = new(StringComparer.OrdinalIgnoreCase) { "mods", "update", "dlcpacks", "x64", "x64a", "x64b", "x64c", "x64d", "x64e", "x64f", "x64g", "x64h", "x64i", "x64j", "x64k", "x64l", "x64m", "x64n", "x64o", "x64p", "x64q", "x64r", "x64s", "x64t", "x64u", "x64v", "x64w" };

    private const string RpfWarning = "RPF-архивы нельзя проверить изнутри: в них могут лежать скрипты. Ставьте паки только из источников, которым доверяете.";
    private const string RulesWarning = "Приложение не знает правил вашего проекта. Графические паки обычно разрешены, а всё, что убирает объекты, делает текстуры прозрачными или даёт преимущество, считается читом и ведёт к бану. Перед игрой проверьте правила проекта.";

    public static ReduxPlan Analyze(IReduxSource source, string? nameOverride = null)
    {
        var paths = source.Entries.Select(e => e.Path.Replace('\\', '/').TrimStart('/')).ToList();
        var (prefix, kind) = FindRoot(paths);

        var files = new List<ReduxFilePlan>();
        var warnings = new List<string>();
        var advantage = new List<string>();
        var replacesGame = new List<string>();

        foreach (var entry in source.Entries)
        {
            var path = entry.Path.Replace('\\', '/').TrimStart('/');
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new ReduxFilePlan(path, path, entry.Length, ReduxVerdict.Skipped, "вне папки пака"));
                continue;
            }

            var gtaPath = prefix.Length > 0 ? path[prefix.Length..] : path;
            var info = ReduxPolicy.Classify(gtaPath, atPackTop: !gtaPath.Contains('/', StringComparison.Ordinal));
            files.Add(new ReduxFilePlan(path, gtaPath, entry.Length, info.Verdict, info.Reason));

            if (info.Verdict == ReduxVerdict.Install)
            {
                if (ReduxPolicy.LooksLikeAdvantageMod(gtaPath))
                {
                    advantage.Add(gtaPath);
                }

                if (!gtaPath.Contains('/', StringComparison.Ordinal) && RootArchive.IsMatch(gtaPath))
                {
                    replacesGame.Add(gtaPath);
                }
            }
        }

        var blocked = files.Count(f => f.Verdict == ReduxVerdict.Blocked);
        if (blocked > 0)
        {
            warnings.Add($"Не будет установлено файлов с кодом или небезопасным путём: {blocked}. Если вам нужен ReShade, ENB или скрипты — ставьте их вручную и только если правила проекта это разрешают.");
        }

        if (advantage.Count > 0)
        {
            warnings.Add($"В названиях файлов есть признаки удаления объектов или прозрачности (например {advantage[0]}). На RP-серверах это считается читом и ведёт к бану.");
        }

        if (replacesGame.Count > 0)
        {
            warnings.Add($"Пак заменяет основные архивы игры: {string.Join(", ", replacesGame.Take(4))}. Оригиналы сохраняются и возвращаются при удалении, но после обновления GTA V пак может перестать работать.");
        }

        if (files.Any(f => f.Verdict == ReduxVerdict.Install && f.GtaPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(RpfWarning);
        }

        if (kind == ReduxRootKind.Unknown)
        {
            warnings.Add("Не удалось определить, где в архиве начинается папка игры (нет mods, update, x64…rpf). Файлы будут скопированы в папку GTA V как лежат в архиве — убедитесь, что это тот самый пак.");
        }

        warnings.Add(RulesWarning);
        return new ReduxPlan
        {
            Name = nameOverride ?? source.Name,
            PayloadPrefix = prefix,
            RootKind = kind,
            Files = files,
            Warnings = warnings,
        };
    }

    /// <summary>The shallowest folder that looks like the GTA V layout; else the lone wrapper folder; else nothing.</summary>
    internal static (string Prefix, ReduxRootKind Kind) FindRoot(IReadOnlyList<string> paths)
    {
        // Candidate prefixes up to four folders deep, with how many files sit under each one's recognisable top level.
        var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var segments = path.Split('/');
            for (var depth = 0; depth <= Math.Min(4, segments.Length - 1); depth++)
            {
                var first = segments[depth];
                var isFile = depth == segments.Length - 1;
                var marker = isFile ? RootArchive.IsMatch(first) : RootFolders.Contains(first);
                if (!marker)
                {
                    continue;
                }

                var prefix = depth == 0 ? string.Empty : string.Join('/', segments.Take(depth)) + "/";
                best[prefix] = best.GetValueOrDefault(prefix) + 1;
            }
        }

        if (best.Count > 0)
        {
            var prefix = best.OrderBy(kv => kv.Key.Count(c => c == '/')).ThenByDescending(kv => kv.Value).First().Key;
            return (prefix, ReduxRootKind.Detected);
        }

        var tops = paths.Select(p => p.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tops.Count == 1 && paths.All(p => p.Contains('/', StringComparison.Ordinal)))
        {
            return (tops[0] + "/", ReduxRootKind.Wrapper);
        }

        return (string.Empty, ReduxRootKind.Unknown);
    }
}
