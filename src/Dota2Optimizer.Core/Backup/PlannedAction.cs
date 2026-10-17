using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

public enum ActionKind
{
    SetRegistry,
    DeleteRegistry,
    WriteFile,
    DeleteFile,
    RunCommand,
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

    public string Target => Kind switch
    {
        ActionKind.SetRegistry or ActionKind.DeleteRegistry => $"{Hive.ShortName()}\\{Key}\\{ValueName}",
        ActionKind.WriteFile or ActionKind.DeleteFile => Path ?? string.Empty,
        _ => $"{Command} {Arguments}",
    };
}
