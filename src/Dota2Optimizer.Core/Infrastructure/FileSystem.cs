using System.Text;

namespace Dota2Optimizer.Core.Infrastructure;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllText(string path, string content);
    void WriteAllBytes(string path, byte[] content);
    void DeleteFile(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern, bool recursive);
    IEnumerable<string> EnumerateDirectories(string directory);
}

public sealed class RealFileSystem : IFileSystem
{
    // Steam/Valve text files are UTF-8 without BOM; keep it that way when writing.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllText(string path, string content)
    {
        EnsureParent(path);
        WriteAtomically(path, Utf8NoBom.GetBytes(content));
    }

    public void WriteAllBytes(string path, byte[] content)
    {
        EnsureParent(path);
        WriteAtomically(path, content);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<string> EnumerateFiles(string directory, string pattern, bool recursive) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            })
            : Enumerable.Empty<string>();

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory, "*", new EnumerationOptions { IgnoreInaccessible = true })
            : Enumerable.Empty<string>();

    private static void EnsureParent(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    // Write to a temp file and swap, so a crash never leaves a half-written config.
    private static void WriteAtomically(string path, byte[] content)
    {
        var tmp = path + ".d2o-tmp";
        File.WriteAllBytes(tmp, content);
        if (File.Exists(path))
        {
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }

            File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}

/// <summary>In-memory file system for tests. Paths are compared case-insensitively like on Windows.</summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    private static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\');

    public bool FileExists(string path) => _files.ContainsKey(Norm(path));

    public bool DirectoryExists(string path)
    {
        var n = Norm(path);
        return _dirs.Contains(n) || _files.Keys.Any(f => f.StartsWith(n + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public string ReadAllText(string path) => Encoding.UTF8.GetString(ReadAllBytes(path));

    public byte[] ReadAllBytes(string path) =>
        _files.TryGetValue(Norm(path), out var b) ? b : throw new FileNotFoundException(path);

    public void WriteAllText(string path, string content) => WriteAllBytes(path, Encoding.UTF8.GetBytes(content));

    public void WriteAllBytes(string path, byte[] content) => _files[Norm(path)] = content.ToArray();

    public void DeleteFile(string path) => _files.Remove(Norm(path));

    public void CreateDirectory(string path) => _dirs.Add(Norm(path));

    public IEnumerable<string> EnumerateFiles(string directory, string pattern, bool recursive)
    {
        var d = Norm(directory) + "\\";
        var regex = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return _files.Keys
            .Where(f => f.StartsWith(d, StringComparison.OrdinalIgnoreCase))
            .Where(f => recursive || !f[d.Length..].Contains('\\'))
            .Where(f => regex.IsMatch(f[(f.LastIndexOf('\\') + 1)..]))
            .ToList();
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        var d = Norm(directory) + "\\";
        return _files.Keys.Concat(_dirs)
            .Where(f => f.StartsWith(d, StringComparison.OrdinalIgnoreCase) && f[d.Length..].Contains('\\'))
            .Select(f => d + f[d.Length..].Split('\\')[0])
            .Concat(_dirs.Where(x => x.StartsWith(d, StringComparison.OrdinalIgnoreCase) && !x[d.Length..].Contains('\\')))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
