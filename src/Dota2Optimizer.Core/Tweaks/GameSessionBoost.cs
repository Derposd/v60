using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>
/// «Ускорение на время игры», like the boost mode of game boosters: while a supported game runs, the
/// «Высокая производительность» power plan is active; when the game closes, the previous plan comes back.
/// Nothing is closed or suspended. The previous plan is kept in a small file, so it is restored even after a crash.
/// </summary>
public sealed partial class GameSessionBoost
{
    private readonly ICommandRunner _cmd;
    private readonly IFileSystem _fs;
    private readonly string _statePath;
    private bool _sessionHandled;

    public GameSessionBoost(ICommandRunner cmd, IFileSystem fs, string statePath)
    {
        _cmd = cmd;
        _fs = fs;
        _statePath = statePath;
    }

    [GeneratedRegex(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeGuid();

    /// <summary>The boost switched the plan and has not restored it yet.</summary>
    public bool Active => _fs.FileExists(_statePath);

    /// <summary>Call periodically. Returns a message when the plan was switched or restored.</summary>
    public string? Update(string? runningGame)
    {
        if (runningGame is null)
        {
            _sessionHandled = false;
            return Restore();
        }

        if (_sessionHandled || Active)
        {
            return null;
        }

        _sessionHandled = true;
        var current = ActiveScheme();
        if (current is null || current == PowerPlanTweak.HighPerformance || current == PowerPlanTweak.UltimatePerformance)
        {
            return null;
        }

        if (!_cmd.Run("powercfg", $"/setactive {PowerPlanTweak.HighPerformance}").Success)
        {
            Log.Warn("Ускорение на время игры: плана «Высокая производительность» нет в системе");
            return null;
        }

        _fs.WriteAllText(_statePath, current.Value.ToString());
        Log.Info($"Ускорение на время игры: {runningGame} запущена, план питания {current} → «Высокая производительность»");
        return $"{runningGame} запущена — включён план питания «Высокая производительность». После игры вернётся прежний.";
    }

    /// <summary>Puts the previous plan back. Skipped if the user picked another plan during the game.</summary>
    public string? Restore()
    {
        if (!Active)
        {
            return null;
        }

        string? message = null;
        try
        {
            if (Guid.TryParse(_fs.ReadAllText(_statePath).Trim(), out var previous) && ActiveScheme() == PowerPlanTweak.HighPerformance &&
                _cmd.Run("powercfg", $"/setactive {previous}").Success)
            {
                Log.Info($"Ускорение на время игры: план питания возвращён ({previous})");
                message = "Игра закрыта — прежний план питания возвращён.";
            }
        }
        finally
        {
            _fs.DeleteFile(_statePath);
        }

        return message;
    }

    private Guid? ActiveScheme()
    {
        var m = SchemeGuid().Match(_cmd.Run("powercfg", "/getactivescheme").StdOut);
        return m.Success ? Guid.Parse(m.Groups[1].Value) : null;
    }
}
