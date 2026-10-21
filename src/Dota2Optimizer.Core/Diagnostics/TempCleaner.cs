using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Diagnostics;

public sealed record TempSummary(long Bytes, int Files)
{
    public string SizeText => Bytes >= 1L << 30 ? $"{Bytes / (double)(1L << 30):0.0} ГБ" : $"{Bytes / (double)(1L << 20):0} МБ";
}

/// <summary>
/// Removes old files from the user's temp folder (what «Очистка диска» calls «Временные файлы»).
/// Only files untouched for a day are removed, files in use are skipped, links are never followed.
/// </summary>
public static class TempCleaner
{
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(1);

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Junctions and symlinks are skipped: never delete anything outside the temp folder.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static string UserTemp => Path.GetTempPath();

    public static TempSummary Measure(string dir, DateTime nowUtc) => Run(dir, nowUtc, delete: false);

    public static TempSummary Clean(string dir, DateTime nowUtc)
    {
        var result = Run(dir, nowUtc, delete: true);
        RemoveEmptyDirectories(dir, nowUtc);
        Log.Info($"Очистка временных файлов: удалено {result.Files} файлов, {result.SizeText}");
        return result;
    }

    private static TempSummary Run(string dir, DateTime nowUtc, bool delete)
    {
        long bytes = 0;
        var files = 0;
        if (!Directory.Exists(dir))
        {
            return new TempSummary(0, 0);
        }

        foreach (var path in Directory.EnumerateFiles(dir, "*", Walk))
        {
            try
            {
                var info = new FileInfo(path);
                if (nowUtc - info.LastWriteTimeUtc < MinAge || nowUtc - info.CreationTimeUtc < MinAge)
                {
                    continue;
                }

                var size = info.Length;
                if (delete)
                {
                    if (info.IsReadOnly)
                    {
                        continue;
                    }

                    info.Delete();
                }

                bytes += size;
                files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by a running program, or not ours to delete.
            }
        }

        return new TempSummary(bytes, files);
    }

    private static void RemoveEmptyDirectories(string dir, DateTime nowUtc)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", Walk).OrderByDescending(d => d.Length))
            {
                try
                {
                    if (nowUtc - Directory.GetLastWriteTimeUtc(sub) >= MinAge && !Directory.EnumerateFileSystemEntries(sub).Any())
                    {
                        Directory.Delete(sub);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
