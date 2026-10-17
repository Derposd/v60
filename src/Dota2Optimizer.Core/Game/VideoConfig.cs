using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.Game;

/// <summary>
/// Text-preserving editor for Dota 2 video.txt. Only keys that already exist in the file are changed;
/// the layout, comments and unknown keys stay byte-for-byte the same.
/// </summary>
public sealed partial class VideoConfig
{
    // "setting.key"<whitespace>"value"
    [GeneratedRegex("^(?<pre>\\s*\"(?<key>[^\"]+)\"\\s+\")(?<val>[^\"]*)(?<post>\".*)$")]
    private static partial Regex LineRegex();

    private readonly List<string> _lines;
    private readonly string _newline;

    private VideoConfig(List<string> lines, string newline, bool endsWithNewline)
    {
        _lines = lines;
        _newline = newline;
        EndsWithNewline = endsWithNewline;
    }

    public bool EndsWithNewline { get; }

    public static VideoConfig Parse(string text)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var endsWithNewline = text.EndsWith('\n');
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (endsWithNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new VideoConfig(lines, newline, endsWithNewline);
    }

    public IReadOnlyDictionary<string, string> Values
    {
        get
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in _lines)
            {
                var m = LineRegex().Match(l);
                if (m.Success)
                {
                    d[m.Groups["key"].Value] = m.Groups["val"].Value;
                }
            }

            return d;
        }
    }

    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public bool Has(string key) => Get(key) is not null;

    /// <summary>Changes an existing key. Returns false (and changes nothing) if the key is not in the file.</summary>
    public bool TrySet(string key, string value)
    {
        var found = false;
        for (var i = 0; i < _lines.Count; i++)
        {
            var m = LineRegex().Match(_lines[i]);
            if (m.Success && string.Equals(m.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = m.Groups["pre"].Value + value + m.Groups["post"].Value;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Finds the first existing key matching a pattern (used where Valve renamed keys between versions).</summary>
    public string? FindKey(Regex pattern) => Values.Keys.FirstOrDefault(pattern.IsMatch);

    public override string ToString() =>
        string.Join(_newline, _lines) + (EndsWithNewline ? _newline : string.Empty);
}
