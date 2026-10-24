namespace Dota2Optimizer.Core.GtaRp;

public enum GtaEdition
{
    Unknown,
    Steam,
    Rockstar,
    Epic,
}

/// <summary>
/// Where GTA V and the RAGE MP client live on this PC. GTA V roleplay projects (GTA5RP, Majestic RP…) run on top of the normal GTA V
/// game files and read the same graphics settings file, so this is all the app needs to know about the machine.
/// </summary>
public sealed record GtaInfo
{
    public const string GameExeName = "GTA5.exe";
    public const string RageMpExeName = "ragemp_v.exe";

    public string? GameDir { get; init; }
    public GtaEdition Edition { get; init; }

    /// <summary>Documents\Rockstar Games\GTA V\settings.xml — GTA creates it on the first launch.</summary>
    public string SettingsPath { get; init; } = string.Empty;
    public bool SettingsExists { get; init; }

    /// <summary>RAGE MP client folder (usually C:\RAGEMP), if installed.</summary>
    public string? RageMpDir { get; init; }
    public bool GameRunning { get; init; }
    public bool RageMpRunning { get; init; }

    public bool GameFound => GameDir is not null;
    public bool AnyRunning => GameRunning || RageMpRunning;
    public string? GameExe => GameDir is null ? null : Path.Combine(GameDir, GameExeName);
    public string? RageMpExe => RageMpDir is null ? null : Path.Combine(RageMpDir, RageMpExeName);
    public string? RageMpCacheDir => RageMpDir is null ? null : Path.Combine(RageMpDir, "client_resources");

    public string EditionRu => Edition switch
    {
        GtaEdition.Steam => "Steam",
        GtaEdition.Rockstar => "Rockstar Games Launcher",
        GtaEdition.Epic => "Epic Games",
        _ => "не определена",
    };
}
