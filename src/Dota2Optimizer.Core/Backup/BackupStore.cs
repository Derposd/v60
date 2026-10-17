using System.Text.Json;
using System.Text.Json.Serialization;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

public enum BackupKind
{
    File,
    Registry,
    Command,
}

/// <summary>What was there before one action ran, and how to put it back.</summary>
public sealed class BackupRecord
{
    public BackupKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }

    public string? Path { get; set; }
    public bool Existed { get; set; }
    public string? BackupFile { get; set; }

    public RegHive Hive { get; set; }
    public string? Key { get; set; }
    public string? ValueName { get; set; }
    public RegValue? Previous { get; set; }

    public string? UndoCommand { get; set; }
    public string? UndoArguments { get; set; }

    [JsonIgnore]
    public string Target => Kind switch
    {
        BackupKind.Registry => $"{Hive.ShortName()}\\{Key}\\{ValueName}",
        BackupKind.File => Path ?? string.Empty,
        _ => $"{UndoCommand} {UndoArguments}",
    };
}

public sealed class ChangeSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TweakId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<BackupRecord> Records { get; set; } = new();
    public bool RolledBack { get; set; }
    public DateTime? RolledBackUtc { get; set; }
    public bool Failed { get; set; }

    [JsonIgnore]
    public bool RequiresAdminToRollback => Records.Any(r => r.RequiresAdmin);
}

/// <summary>
/// Persistent journal of every change made by the app. Records are written to disk
/// before the corresponding change is executed (write-ahead), so even a crash mid-apply
/// leaves enough information to roll back.
/// </summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IFileSystem _fs;
    private readonly object _gate = new();

    public BackupStore(IFileSystem fs, string directory)
    {
        _fs = fs;
        Directory = directory;
    }

    public string Directory { get; }

    private string ManifestPath => System.IO.Path.Combine(Directory, "manifest.json");

    public IReadOnlyList<ChangeSet> Load()
    {
        lock (_gate)
        {
            if (!_fs.FileExists(ManifestPath))
            {
                return new List<ChangeSet>();
            }

            return JsonSerializer.Deserialize<List<ChangeSet>>(_fs.ReadAllText(ManifestPath), JsonOptions) ?? new List<ChangeSet>();
        }
    }

    public ChangeSet? Find(string id) => Load().FirstOrDefault(c => c.Id == id);

    public IReadOnlyList<ChangeSet> Active() => Load().Where(c => !c.RolledBack).ToList();

    /// <summary>Inserts or replaces a change set and saves the manifest.</summary>
    public void Save(ChangeSet set)
    {
        lock (_gate)
        {
            var all = Load().ToList();
            var idx = all.FindIndex(c => c.Id == set.Id);
            if (idx >= 0)
            {
                all[idx] = set;
            }
            else
            {
                all.Add(set);
            }

            _fs.CreateDirectory(Directory);
            _fs.WriteAllText(ManifestPath, JsonSerializer.Serialize(all, JsonOptions));
        }
    }

    /// <summary>Copies a file into the backup folder and returns the record describing it.</summary>
    public BackupRecord BackupFile(ChangeSet set, string path, string description, bool requiresAdmin)
    {
        var record = new BackupRecord
        {
            Kind = BackupKind.File,
            Path = path,
            Description = description,
            RequiresAdmin = requiresAdmin,
            Existed = _fs.FileExists(path),
        };
        if (record.Existed)
        {
            var file = System.IO.Path.Combine(Directory, set.Id, $"{set.Records.Count:D3}_{PathEx.FileName(path)}");
            _fs.WriteAllBytes(file, _fs.ReadAllBytes(path));
            record.BackupFile = file;
        }

        return record;
    }

    /// <summary>Change sets applied after <paramref name="id"/> that touch the same targets; rolling back out of order would clobber them.</summary>
    public IReadOnlyList<ChangeSet> LaterConflicts(string id)
    {
        var all = Load();
        var set = all.FirstOrDefault(c => c.Id == id);
        if (set is null)
        {
            return Array.Empty<ChangeSet>();
        }

        var targets = set.Records.Select(r => r.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(c => !c.RolledBack && c.CreatedUtc > set.CreatedUtc && c.Records.Any(r => targets.Contains(r.Target)))
            .ToList();
    }
}
