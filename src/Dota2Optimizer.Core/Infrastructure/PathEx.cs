namespace Dota2Optimizer.Core.Infrastructure;

public static class PathEx
{
    /// <summary>File name for Windows paths regardless of the OS the code runs on (unit tests run on Linux too).</summary>
    public static string FileName(string path)
    {
        var i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? path : path[(i + 1)..];
    }

    /// <summary>Parent folder for Windows paths on any OS (null at the root).</summary>
    public static string? Parent(string path)
    {
        var p = path.TrimEnd('\\', '/');
        var i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i <= 0 ? null : p[..i];
    }

    /// <summary>Joins with a backslash, the separator of the paths this app works with.</summary>
    public static string Join(string dir, string rel) => dir.TrimEnd('\\', '/') + "\\" + rel.TrimStart('\\', '/');

    /// <summary>Path of <paramref name="path"/> relative to <paramref name="root"/> (which must contain it).</summary>
    public static string Relative(string root, string path) => path[root.TrimEnd('\\', '/').Length..].TrimStart('\\', '/');
}
