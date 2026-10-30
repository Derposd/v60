using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

public enum ProfileKind
{
    Games,
    Work,
}

/// <summary>
/// «Игры» / «Работа». The games profile is simply the set of switches this app turned on; switching to «Работа» remembers that set and
/// rolls it back (and pauses the real-time game mode), switching to «Игры» turns the remembered set on again.
/// </summary>
public sealed class ProfileState
{
    public ProfileKind Active { get; set; } = ProfileKind.Games;
    public List<string> GameTweaks { get; set; } = new();

    /// <summary>The real-time game mode was on when «Работа» was chosen, so «Игры» turns it back on.</summary>
    public bool GameModeWasOn { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static ProfileState Load(IFileSystem fs, string path)
    {
        try
        {
            return fs.FileExists(path) ? JsonSerializer.Deserialize<ProfileState>(fs.ReadAllText(path), Json) ?? new ProfileState() : new ProfileState();
        }
        catch (JsonException)
        {
            return new ProfileState();
        }
    }

    public void Save(IFileSystem fs, string path) => fs.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    /// <summary>Ids to turn on for «Игры»: the remembered set, or — the first time — the recommended tweaks.</summary>
    public IReadOnlyList<string> ToApply(IEnumerable<TweakToggle> toggles)
    {
        var list = toggles.ToList();
        var ids = GameTweaks.Count > 0
            ? GameTweaks.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : list.Where(t => t.Tweak.Recommended && !t.Tweak.Disputed).Select(t => t.Tweak.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.Where(t => ids.Contains(t.Tweak.Id) && t.CanTurnOn).Select(t => t.Tweak.Id).ToList();
    }

    /// <summary>Ids to roll back for «Работа»: everything this app turned on that has its own backup.</summary>
    public static IReadOnlyList<string> ToRevert(IEnumerable<TweakToggle> toggles) =>
        toggles.Where(t => t.CanTurnOff).Select(t => t.Tweak.Id).ToList();
}
