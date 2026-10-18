using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Benchmark;

public sealed record HistoryEntry(DateTime Time, string Kind, string Title, string Details);

/// <summary>History of applied changes and match reports, stored locally as JSON.</summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly IFileSystem _fs;
    private readonly string _path;

    public HistoryStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    private sealed class Data
    {
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    private Data Load() =>
        _fs.FileExists(_path) ? JsonSerializer.Deserialize<Data>(_fs.ReadAllText(_path), Options) ?? new Data() : new Data();

    private void Save(Data d) => _fs.WriteAllText(_path, JsonSerializer.Serialize(d, Options));

    public IReadOnlyList<HistoryEntry> Entries => Load().Entries;

    public void Add(HistoryEntry e)
    {
        var d = Load();
        d.Entries.Add(e);
        Save(d);
    }
}
