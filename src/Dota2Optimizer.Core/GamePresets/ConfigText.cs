using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Game;

namespace Dota2Optimizer.Core.GamePresets;

public enum ConfigFormat
{
    /// <summary>Unreal Engine GameUserSettings.ini: [Section] then key=value.</summary>
    Ini,

    /// <summary>Minecraft Java options.txt: key:value.</summary>
    Colon,

    /// <summary>Source-style "key" "value" (Apex Legends videoconfig.txt).</summary>
    SourceVideo,
}

/// <summary>
/// Text-preserving config editor: only keys that already exist change, everything else (order, comments, unknown keys,
/// line endings) stays byte for byte. Nothing is ever added, so a preset can't create settings the game doesn't expect.
/// </summary>
public abstract partial class ConfigText
{
    protected List<string> Lines { get; }
    private readonly string _newline;
    private readonly bool _endsWithNewline;

    protected ConfigText(string text)
    {
        _newline = text.Contains("\r\n") ? "\r\n" : "\n";
        _endsWithNewline = text.EndsWith('\n');
        Lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (_endsWithNewline)
        {
            Lines.RemoveAt(Lines.Count - 1);
        }
    }

    public static ConfigText Parse(ConfigFormat format, string text) => format switch
    {
        ConfigFormat.Ini => new IniText(text),
        ConfigFormat.Colon => new ColonText(text),
        _ => new SourceText(text),
    };

    public abstract string? Get(string? section, string key);

    /// <summary>Sets an existing key; false when it isn't in the file.</summary>
    public abstract bool TrySet(string? section, string key, string value);

    public override string ToString() => string.Join(_newline, Lines) + (_endsWithNewline ? _newline : string.Empty);

    private sealed partial class IniText : ConfigText
    {
        public IniText(string text)
            : base(text)
        {
        }

        [GeneratedRegex(@"^\s*\[(?<s>[^\]]+)\]\s*$")]
        private static partial Regex Section();

        [GeneratedRegex(@"^(?<k>\s*[^=;#\s][^=]*?)\s*=(?<v>.*)$")]
        private static partial Regex Pair();

        private IEnumerable<(int Index, string Key, string Value)> Find(string? section)
        {
            string? current = null;
            for (var i = 0; i < Lines.Count; i++)
            {
                var s = Section().Match(Lines[i]);
                if (s.Success)
                {
                    current = s.Groups["s"].Value.Trim();
                    continue;
                }

                var p = Pair().Match(Lines[i]);
                if (p.Success && (section is null || string.Equals(current, section, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return (i, p.Groups["k"].Value.Trim(), p.Groups["v"].Value);
                }
            }
        }

        public override string? Get(string? section, string key) =>
            Find(section).Where(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();

        public override bool TrySet(string? section, string key, string value)
        {
            var hits = Find(section).Where(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var (i, _, _) in hits)
            {
                Lines[i] = Lines[i][..(Lines[i].IndexOf('=') + 1)] + value;
            }

            return hits.Count > 0;
        }
    }

    private sealed partial class ColonText : ConfigText
    {
        public ColonText(string text)
            : base(text)
        {
        }

        [GeneratedRegex(@"^(?<k>[A-Za-z0-9_.]+):(?<v>.*)$")]
        private static partial Regex Pair();

        public override string? Get(string? section, string key)
        {
            foreach (var l in Lines)
            {
                var m = Pair().Match(l);
                if (m.Success && m.Groups["k"].Value == key)
                {
                    return m.Groups["v"].Value;
                }
            }

            return null;
        }

        public override bool TrySet(string? section, string key, string value)
        {
            for (var i = 0; i < Lines.Count; i++)
            {
                var m = Pair().Match(Lines[i]);
                if (!m.Success || m.Groups["k"].Value != key)
                {
                    continue;
                }

                Lines[i] = $"{key}:{Shape(m.Groups["v"].Value, value)}";
                return true;
            }

            return false;
        }

        /// <summary>Keeps the value in the style the game wrote it: quoted strings stay quoted, old numeric booleans stay numeric.</summary>
        private static string Shape(string current, string wanted)
        {
            if (current.StartsWith('"') && !wanted.StartsWith('"'))
            {
                return $"\"{wanted}\"";
            }

            if (current.Length > 0 && current.All(char.IsDigit) && wanted is "true" or "false")
            {
                return wanted == "true" ? "2" : "0";
            }

            return wanted;
        }
    }

    private sealed class SourceText : ConfigText
    {
        private readonly VideoConfig _cfg;

        public SourceText(string text)
            : base(text)
        {
            _cfg = VideoConfig.Parse(text);
        }

        public override string? Get(string? section, string key) => _cfg.Get(key);

        public override bool TrySet(string? section, string key, string value) => _cfg.TrySet(key, value);

        public override string ToString() => _cfg.ToString();
    }
}
