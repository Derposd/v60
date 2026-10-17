using System.Globalization;
using Microsoft.Win32;

namespace Dota2Optimizer.Core.Infrastructure;

public enum RegHive
{
    CurrentUser,
    LocalMachine,
}

public enum RegKind
{
    String,
    ExpandString,
    DWord,
    QWord,
    MultiString,
    Binary,
}

/// <summary>
/// Registry value in a serializable form: numbers are decimal strings, binary is hex,
/// multi-strings are joined with '\n'. This keeps backups human-readable JSON.
/// </summary>
public sealed record RegValue(RegKind Kind, string Data)
{
    public static RegValue Dword(int value) => new(RegKind.DWord, value.ToString(CultureInfo.InvariantCulture));
    public static RegValue Str(string value) => new(RegKind.String, value);

    public int? AsInt() =>
        Kind is RegKind.DWord or RegKind.QWord && long.TryParse(Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? (int)v
            : null;

    public override string ToString() => $"{Kind}:{Data}";
}

public interface IRegistry
{
    RegValue? GetValue(RegHive hive, string key, string name);
    void SetValue(RegHive hive, string key, string name, RegValue value);
    void DeleteValue(RegHive hive, string key, string name);
    IReadOnlyList<string> GetValueNames(RegHive hive, string key);
    IReadOnlyList<string> GetSubKeyNames(RegHive hive, string key);
}

public static class RegHiveExtensions
{
    public static string ShortName(this RegHive hive) => hive == RegHive.CurrentUser ? "HKCU" : "HKLM";
}

/// <summary>Real Windows registry (64-bit view).</summary>
public sealed class WindowsRegistry : IRegistry
{
    private static RegistryKey Base(RegHive hive) =>
        RegistryKey.OpenBaseKey(hive == RegHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public RegValue? GetValue(RegHive hive, string key, string name)
    {
        using var root = Base(hive);
        using var k = root.OpenSubKey(key, writable: false);
        if (k is null)
        {
            return null;
        }

        var raw = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return null;
        }

        return k.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => new RegValue(RegKind.DWord, Convert.ToUInt32(unchecked((uint)(int)raw)).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new RegValue(RegKind.QWord, Convert.ToInt64(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.ExpandString => new RegValue(RegKind.ExpandString, (string)raw),
            RegistryValueKind.MultiString => new RegValue(RegKind.MultiString, string.Join('\n', (string[])raw)),
            RegistryValueKind.Binary => new RegValue(RegKind.Binary, Convert.ToHexString((byte[])raw)),
            _ => new RegValue(RegKind.String, raw.ToString() ?? string.Empty),
        };
    }

    public void SetValue(RegHive hive, string key, string name, RegValue value)
    {
        using var root = Base(hive);
        using var k = root.CreateSubKey(key, writable: true)
            ?? throw new InvalidOperationException($"Не удалось открыть ключ {hive.ShortName()}\\{key}");
        switch (value.Kind)
        {
            case RegKind.DWord:
                k.SetValue(name, unchecked((int)uint.Parse(value.Data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                break;
            case RegKind.QWord:
                k.SetValue(name, long.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.QWord);
                break;
            case RegKind.ExpandString:
                k.SetValue(name, value.Data, RegistryValueKind.ExpandString);
                break;
            case RegKind.MultiString:
                k.SetValue(name, value.Data.Split('\n'), RegistryValueKind.MultiString);
                break;
            case RegKind.Binary:
                k.SetValue(name, Convert.FromHexString(value.Data), RegistryValueKind.Binary);
                break;
            default:
                k.SetValue(name, value.Data, RegistryValueKind.String);
                break;
        }
    }

    public void DeleteValue(RegHive hive, string key, string name)
    {
        using var root = Base(hive);
        using var k = root.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string key)
    {
        using var root = Base(hive);
        using var k = root.OpenSubKey(key, writable: false);
        return k?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string key)
    {
        using var root = Base(hive);
        using var k = root.OpenSubKey(key, writable: false);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }
}

/// <summary>In-memory registry used by tests and by dry-run previews.</summary>
public sealed class InMemoryRegistry : IRegistry
{
    private readonly Dictionary<string, RegValue> _values = new(StringComparer.OrdinalIgnoreCase);

    private static string Id(RegHive hive, string key, string name) => $"{hive}\\{key.TrimEnd('\\')}\\\u0001{name}";

    public RegValue? GetValue(RegHive hive, string key, string name) =>
        _values.TryGetValue(Id(hive, key, name), out var v) ? v : null;

    public void SetValue(RegHive hive, string key, string name, RegValue value) => _values[Id(hive, key, name)] = value;

    public void DeleteValue(RegHive hive, string key, string name) => _values.Remove(Id(hive, key, name));

    public IReadOnlyList<string> GetValueNames(RegHive hive, string key)
    {
        var prefix = $"{hive}\\{key.TrimEnd('\\')}\\\u0001";
        return _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..]).ToList();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string key)
    {
        var prefix = $"{hive}\\{key.TrimEnd('\\')}\\";
        return _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Where(rest => !rest.StartsWith('\u0001'))
            .Select(rest => rest.Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
