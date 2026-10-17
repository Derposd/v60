namespace Dota2Optimizer.Core.Infrastructure;

public static class PathEx
{
    /// <summary>File name for Windows paths regardless of the OS the code runs on (unit tests run on Linux too).</summary>
    public static string FileName(string path)
    {
        var i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? path : path[(i + 1)..];
    }
}
