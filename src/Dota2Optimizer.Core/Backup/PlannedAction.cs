using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

public enum ActionKind
{
    SetRegistry,
    DeleteRegistry,
    WriteFile,
    DeleteFile,
    RunCommand,

    /// <summary>Changes a service's start type (and optionally stops it); the old type and running state are restored.</summary>
    SetService,

    /// <summary>Copies a binary file over a target (a DLL into a game folder); the target is backed up first.</summary>
    CopyFile,
}

/// <summary>
/// One atomic change. Kept as a flat serializable record so a plan can be handed to the
/// elevated helper process as JSON and executed there with the same backup logic.
/// </summary>
public sealed record PlannedAction
{
    public ActionKind Kind { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool RequiresAdmin { get; init; }

    // Registry
    public RegHive Hive { get; init; }
    public string? Key { get; init; }
    public string? ValueName { get; init; }
    public RegValue? Value { get; init; }

    // File
    public string? Path { get; init; }
    public string? Content { get; init; }

    // Command (with explicit undo command captured at planning time)
    public string? Command { get; init; }
    public string? Arguments { get; init; }
    public string? UndoCommand { get; init; }
    public string? UndoArguments { get; init; }

    // Service
    public string? ServiceName { get; init; }
    public string? ServiceStartType { get; init; }
    public bool StopService { get; init; }

    // Copy
    public string? SourcePath { get; init; }

    public static PlannedAction SetReg(RegHive hive, string key, string name, RegValue value, string description) => new()
    {
        Kind = ActionKind.SetRegistry,
        Hive = hive,
        Key = key,
        ValueName = name,
        Value = value,
        Description = description,
        RequiresAdmin = hive == RegHive.LocalMachine,
    };

    public static PlannedAction DeleteReg(RegHive hive, string key, string name, string description) => new()
    {
        Kind = ActionKind.DeleteRegistry,
        Hive = hive,
        Key = key,
        ValueName = name,
        Description = description,
        RequiresAdmin = hive == RegHive.LocalMachine,
    };

    public static PlannedAction WriteFile(string path, string content, string description, bool requiresAdmin = false) => new()
    {
        Kind = ActionKind.WriteFile,
        Path = path,
        Content = content,
        Description = description,
        RequiresAdmin = requiresAdmin,
    };

    /// <summary>Deletes a file after backing it up; rollback puts it back.</summary>
    public static PlannedAction DeleteFile(string path, string description) => new()
    {
        Kind = ActionKind.DeleteFile,
        Path = path,
        Description = description,
    };

    public static PlannedAction Run(string command, string args, string undoCommand, string undoArgs, string description, bool requiresAdmin) => new()
    {
        Kind = ActionKind.RunCommand,
        Command = command,
        Arguments = args,
        UndoCommand = undoCommand,
        UndoArguments = undoArgs,
        Description = description,
        RequiresAdmin = requiresAdmin,
    };

    /// <summary>Sets a service's start type ("demand", "disabled"…). Stopping it right away is optional; undo starts it again if it was running.</summary>
    public static PlannedAction SetService(string name, string startType, bool stop, string description) => new()
    {
        Kind = ActionKind.SetService,
        ServiceName = name,
        ServiceStartType = startType,
        StopService = stop,
        Description = description,
        RequiresAdmin = true,
    };

    public static PlannedAction CopyFile(string source, string target, string description, bool requiresAdmin = false) => new()
    {
        Kind = ActionKind.CopyFile,
        SourcePath = source,
        Path = target,
        Description = description,
        RequiresAdmin = requiresAdmin,
    };

    public string Target => Kind switch
    {
        ActionKind.SetRegistry or ActionKind.DeleteRegistry => $"{Hive.ShortName()}\\{Key}\\{ValueName}",
        ActionKind.WriteFile or ActionKind.DeleteFile or ActionKind.CopyFile => Path ?? string.Empty,
        ActionKind.SetService => $"служба {ServiceName}",
        _ => $"{Command} {Arguments}",
    };
}
