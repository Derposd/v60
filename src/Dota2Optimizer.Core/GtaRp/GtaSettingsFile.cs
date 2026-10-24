using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Dota2Optimizer.Core.GtaRp;

/// <summary>
/// GTA V's settings.xml (Documents\Rockstar Games\GTA V). Edited as text, so everything outside the changed value="…" attributes
/// stays byte-for-byte as the game wrote it. Only keys that already exist are touched: a key the game does not know is never invented.
/// </summary>
public sealed class GtaSettingsFile
{
    private string _text;

    private GtaSettingsFile(string text)
    {
        _text = text;
    }

    public string Text => _text;

    /// <summary>Parses and validates the file; a broken file is never edited.</summary>
    public static GtaSettingsFile Parse(string text)
    {
        try
        {
            var doc = XDocument.Parse(text);
            if (doc.Root?.Name.LocalName != "Settings")
            {
                throw new InvalidDataException("В settings.xml нет корневого элемента <Settings>.");
            }
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException($"settings.xml повреждён: {ex.Message}", ex);
        }

        return new GtaSettingsFile(text);
    }

    private static Regex Pattern(string key) =>
        new($@"(?<pre><{Regex.Escape(key)}\s+value\s*=\s*"")(?<v>[^""]*)(?<post>"")", RegexOptions.CultureInvariant);

    public string? Get(string key)
    {
        var m = Pattern(key).Match(_text);
        return m.Success ? m.Groups["v"].Value : null;
    }

    public bool Has(string key) => Get(key) is not null;

    /// <summary>True when the key exists and already holds the value (numbers compare as numbers: 0.4 equals 0.400000).</summary>
    public bool Is(string key, string value) => Get(key) is { } current && Same(current, value);

    /// <summary>Sets an existing key. Returns true only when something really changed.</summary>
    public bool TrySet(string key, string value)
    {
        var regex = Pattern(key);
        var m = regex.Match(_text);
        if (!m.Success || Same(m.Groups["v"].Value, value))
        {
            return false;
        }

        var written = FormatLike(m.Groups["v"].Value, value);
        _text = regex.Replace(_text, m2 => m2.Groups["pre"].Value + written + m2.Groups["post"].Value, 1);
        return true;
    }

    private static bool Same(string a, string b)
    {
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return Math.Abs(x - y) < 1e-6;
        }

        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Floats are written the way the game writes them (six decimals), integers and booleans as they are.</summary>
    private static string FormatLike(string current, string value)
    {
        if (current.Contains('.', StringComparison.Ordinal) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d.ToString("F6", CultureInfo.InvariantCulture);
        }

        return value;
    }
}
