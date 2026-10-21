using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.SystemInfo;

/// <summary>
/// Keeps the last scan on disk so the app does not have to scan the PC again on every start.
/// A local JSON file in the app's data folder; nothing leaves the machine.
/// </summary>
public sealed class SnapshotCache
{
    /// <summary>Bump when the snapshot shape changes: older files are then ignored and a fresh scan is made.</summary>
    private const int FormatVersion = 1;

    // Tuples (power plans) are fields, not properties.
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IncludeFields = true };

    private readonly IFileSystem _fs;
    private readonly string _path;

    public SnapshotCache(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    private sealed class Data
    {
        public int Version { get; set; }
        public SystemSnapshot? Snapshot { get; set; }
    }

    /// <summary>The saved scan, or null when there is none or it is unreadable or from an older version.</summary>
    public SystemSnapshot? Load()
    {
        try
        {
            if (!_fs.FileExists(_path))
            {
                return null;
            }

            var data = JsonSerializer.Deserialize<Data>(_fs.ReadAllText(_path), Options);
            return data?.Version == FormatVersion ? data.Snapshot : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn($"Сохранённый скан не прочитан, будет новое сканирование: {ex.Message}");
            return null;
        }
    }

    public void Save(SystemSnapshot snapshot)
    {
        try
        {
            _fs.WriteAllText(_path, JsonSerializer.Serialize(new Data { Version = FormatVersion, Snapshot = snapshot }, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Only a convenience: the next start scans again.
            Log.Warn($"Не удалось сохранить скан: {ex.Message}");
        }
    }
}
