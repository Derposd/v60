using System.Text.Json;
using System.Text.Json.Serialization;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

public sealed class ElevatedItem
{
    public string TweakId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<PlannedAction> Actions { get; set; } = new();
}

public sealed class ElevatedRequest
{
    public string Mode { get; set; } = "apply"; // apply | rollback | restorePoint | boost
    public string? BoostId { get; set; }
    public bool CreateRestorePoint { get; set; }
    public List<ElevatedItem> Items { get; set; } = new();
    public List<string> ChangeSetIds { get; set; } = new();
    public string BackupDir { get; set; } = string.Empty;
    public string ResultPath { get; set; } = string.Empty;
}

public sealed class ElevatedItemResult
{
    public string TweakId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ChangeSetId { get; set; }
    public string? Error { get; set; }
}

public sealed class ElevatedResult
{
    public bool? RestorePointCreated { get; set; }
    public string? RestorePointMessage { get; set; }
    public List<ElevatedItemResult> Items { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// Protocol between the normal UI process and an elevated copy of the same exe.
/// The UI writes a request JSON, starts "Dota2Optimizer.exe --elevated request.json" through UAC
/// and reads the result JSON. Only admin-level actions go through here.
/// </summary>
public static class ElevatedHost
{
    public const string Argument = "--elevated";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string WriteRequest(ElevatedRequest request)
    {
        var dir = Path.Combine(AppPaths.Root, "elevated");
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(dir, $"request-{id}.json");
        request.ResultPath = Path.Combine(dir, $"result-{id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request, Json));
        return path;
    }

    public static ElevatedResult? ReadResult(ElevatedRequest request)
    {
        if (!File.Exists(request.ResultPath))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(request.ResultPath), Json);
        TryDelete(request.ResultPath);
        return result;
    }

    /// <summary>Entry point in the elevated process (one request from a file).</summary>
    public static int Run(string requestPath)
    {
        var request = JsonSerializer.Deserialize<ElevatedRequest>(File.ReadAllText(requestPath), Json)
                      ?? throw new InvalidOperationException("Пустой запрос");
        TryDelete(requestPath);

        // Only accept result paths inside our own data folder.
        if (!InsideRoot(request.ResultPath))
        {
            throw new InvalidOperationException("Недопустимый путь в запросе");
        }

        var result = Execute(request);
        File.WriteAllText(request.ResultPath, JsonSerializer.Serialize(result, Json));
        return 0;
    }

    /// <summary>Runs one request with backups. Used by the one-shot helper and by the session helper (<see cref="ElevatedServer"/>).</summary>
    public static ElevatedResult Execute(ElevatedRequest request)
    {
        if (!InsideRoot(request.BackupDir))
        {
            throw new InvalidOperationException("Недопустимый путь в запросе");
        }

        var result = new ElevatedResult();
        var executor = new ChangeExecutor(new RealFileSystem(), new WindowsRegistry(), new ProcessCommandRunner(), new BackupStore(new RealFileSystem(), request.BackupDir));

        if (request.CreateRestorePoint || request.Mode == "restorePoint")
        {
            var rp = RestorePoint.Create("FrameLab: перед изменениями");
            result.RestorePointCreated = rp.Success;
            result.RestorePointMessage = rp.Message;
        }

        switch (request.Mode)
        {
            case "apply":
                foreach (var item in request.Items)
                {
                    var r = executor.Apply(item.TweakId, item.Title, item.Actions, dryRun: false);
                    result.Items.Add(new ElevatedItemResult { TweakId = item.TweakId, Success = r.Success, ChangeSetId = r.ChangeSetId, Error = r.Error });
                }

                break;
            case "rollback":
                foreach (var id in request.ChangeSetIds)
                {
                    var r = executor.Rollback(id);
                    result.Items.Add(new ElevatedItemResult { ChangeSetId = id, Success = r.Success });
                    result.Messages.AddRange(r.Messages);
                }

                break;
            case "boost":
                var b = RunBoost(request.BoostId);
                result.Items.Add(new ElevatedItemResult { TweakId = request.BoostId ?? string.Empty, Success = b.Success, Error = b.Success ? null : b.Message });
                result.Messages.Add(b.Message);
                break;
        }

        return result;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Line);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Line);

    private static readonly JsonSerializerOptions Line = new() { Converters = { new JsonStringEnumConverter() } };

    private static bool InsideRoot(string path) =>
        !string.IsNullOrEmpty(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase);

    /// <summary>One-off admin actions; only the known ids are accepted.</summary>
    private static Boost.BoostResult RunBoost(string? id) => id switch
    {
        Boost.BoostCatalog.Standby => Boost.MemoryTools.PurgeStandbyList(),
        Boost.BoostCatalog.Trim => Trim(),
        _ => new Boost.BoostResult(false, $"Неизвестное действие: {id}"),
    };

    private static Boost.BoostResult Trim()
    {
        var r = new ProcessCommandRunner().Run("defrag.exe", "/C /L", TimeSpan.FromMinutes(10));
        return r.Success
            ? new Boost.BoostResult(true, "TRIM выполнен для всех SSD.")
            : new Boost.BoostResult(false, $"defrag вернул код {r.ExitCode}: {r.StdOut.Trim().Split('\n').LastOrDefault()}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
