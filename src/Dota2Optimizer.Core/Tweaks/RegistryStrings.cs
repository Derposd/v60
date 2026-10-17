namespace Dota2Optimizer.Core.Tweaks;

/// <summary>Helpers that update one setting inside multi-setting registry strings without losing the others.</summary>
public static class RegistryStrings
{
    /// <summary>
    /// UserGpuPreferences values look like "AutoHDREnable=0;GpuPreference=2;". Sets one key and keeps the rest.
    /// </summary>
    public static string SetSemicolonPair(string? existing, string key, string value)
    {
        var pairs = (existing ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        pairs.Add($"{key}={value}");
        return string.Join(";", pairs) + ";";
    }

    public static string? GetSemicolonPair(string? existing, string key) =>
        (existing ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[(key.Length + 1)..])
            .FirstOrDefault();

    /// <summary>
    /// AppCompatFlags\Layers values look like "~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE".
    /// Adds a flag, keeps other flags and the leading "~" marker.
    /// </summary>
    public static string AddLayerFlag(string? existing, string flag)
    {
        var tokens = (existing ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var hasTilde = tokens.Remove("~") || tokens.Count == 0;
        if (!tokens.Contains(flag, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(flag);
        }

        return (hasTilde ? "~ " : string.Empty) + string.Join(' ', tokens);
    }

    public static bool HasLayerFlag(string? existing, string flag) =>
        (existing ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(flag, StringComparer.OrdinalIgnoreCase);
}
