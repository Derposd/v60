using System.IO.Compression;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Redux;

/// <summary>One file inside a redux pack. <see cref="Path"/> uses "/" separators and is relative to the pack, not yet to GTA V.</summary>
public sealed record ReduxEntry(string Path, long Length);

/// <summary>Where a pack comes from: a folder, a .zip, or an archive that was unpacked to a staging folder.</summary>
public interface IReduxSource : IDisposable
{
    string Name { get; }
    IReadOnlyList<ReduxEntry> Entries { get; }
    Stream Open(ReduxEntry entry);
}

public sealed class FolderReduxSource : IReduxSource
{
    private readonly string _root;
    private readonly bool _deleteOnDispose;
    private readonly List<ReduxEntry> _entries = new();

    public FolderReduxSource(string root, string? name = null, bool deleteOnDispose = false)
    {
        _root = Path.GetFullPath(root);
        _deleteOnDispose = deleteOnDispose;
        Name = name ?? new DirectoryInfo(_root).Name;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Links could point anywhere on the disk; they are never followed.
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var file in Directory.EnumerateFiles(_root, "*", options))
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rel = Path.GetRelativePath(_root, full).Replace('\\', '/');
            _entries.Add(new ReduxEntry(rel, new FileInfo(full).Length));
        }
    }

    public string Name { get; }
    public IReadOnlyList<ReduxEntry> Entries => _entries;

    public Stream Open(ReduxEntry entry) =>
        new FileStream(Path.Combine(_root, entry.Path.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);

    public void Dispose()
    {
        if (!_deleteOnDispose)
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось удалить временную папку {_root}: {ex.Message}");
        }
    }
}

public sealed class ZipReduxSource : IReduxSource
{
    /// <summary>A real redux is a few GB to a few dozen; this only stops absurd archives (zip bombs).</summary>
    public const long MaxUncompressedBytes = 120L * 1024 * 1024 * 1024;
    public const int MaxEntries = 300_000;

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _byPath = new(StringComparer.Ordinal);
    private readonly List<ReduxEntry> _entries = new();

    public ZipReduxSource(string path)
    {
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        _zip = ZipFile.OpenRead(path);
        long total = 0;
        foreach (var e in _zip.Entries)
        {
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\'))
            {
                continue;
            }

            if (_entries.Count >= MaxEntries)
            {
                _zip.Dispose();
                throw new InvalidDataException($"В архиве больше {MaxEntries} файлов — это не похоже на пак графики.");
            }

            total += e.Length;
            if (total > MaxUncompressedBytes)
            {
                _zip.Dispose();
                throw new InvalidDataException("Распакованный размер архива превышает 120 ГБ — это не похоже на пак графики.");
            }

            var rel = e.FullName.Replace('\\', '/');
            if (_byPath.TryAdd(rel, e))
            {
                _entries.Add(new ReduxEntry(rel, e.Length));
            }
        }
    }

    public string Name { get; }
    public IReadOnlyList<ReduxEntry> Entries => _entries;
    public Stream Open(ReduxEntry entry) => _byPath[entry.Path].Open();
    public void Dispose() => _zip.Dispose();
}

/// <summary>Opens a pack given as a folder, a .zip, or (through 7-Zip / the system tar) a .7z / .rar.</summary>
public static class ReduxSources
{
    private static readonly string[] SevenZipPaths =
    {
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe",
    };

    public static IReduxSource Open(string path, ICommandRunner commands, IFileSystem fs, string stagingRoot)
    {
        if (Directory.Exists(path))
        {
            return new FolderReduxSource(path);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл или папка не найдены.", path);
        }

        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new ZipReduxSource(path);
        }

        return Unpack(path, commands, fs, stagingRoot);
    }

    private static IReduxSource Unpack(string archive, ICommandRunner commands, IFileSystem fs, string stagingRoot)
    {
        var staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var timeout = TimeSpan.FromHours(2);

        var sevenZip = SevenZipPaths.FirstOrDefault(fs.FileExists);
        var result = sevenZip is not null
            ? commands.Run(sevenZip, $"x -y -bd \"-o{staging}\" \"{archive}\"", timeout)
            : commands.Run("tar", $"-xf \"{archive}\" -C \"{staging}\"", timeout);

        if (!result.Success)
        {
            TryDelete(staging);
            throw new InvalidDataException(
                $"Не удалось распаковать «{Path.GetFileName(archive)}». Установите бесплатный 7-Zip или распакуйте архив сами и выберите папку.");
        }

        return new FolderReduxSource(staging, Path.GetFileNameWithoutExtension(archive), deleteOnDispose: true);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось удалить {dir}: {ex.Message}");
        }
    }
}
