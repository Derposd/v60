using System.Text;

namespace Dota2Optimizer.Core.Vdf;

/// <summary>
/// Valve KeyValues (VDF) node. A node is either a leaf with a string value or an object
/// with ordered children. Keys are case-insensitive, as Steam treats them.
/// </summary>
public sealed class VdfNode
{
    private readonly List<VdfNode> _children = new();

    public VdfNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; set; }

    /// <summary>Leaf value; null for objects.</summary>
    public string? Value { get; set; }

    public bool IsObject => Value is null;

    public IReadOnlyList<VdfNode> Children => _children;

    public VdfNode? this[string key] => Child(key);

    public VdfNode? Child(string key) =>
        _children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    public string? ValueOf(string key) => Child(key)?.Value;

    /// <summary>Follows a path of keys, e.g. ("Software", "Valve", "Steam").</summary>
    public VdfNode? Path(params string[] keys)
    {
        var node = this;
        foreach (var k in keys)
        {
            node = node.Child(k);
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    public VdfNode Add(VdfNode child)
    {
        _children.Add(child);
        return child;
    }

    public bool Remove(string key) =>
        _children.RemoveAll(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>Returns the child object with this key, creating it (and turning a leaf into an object) if needed.</summary>
    public VdfNode GetOrAddObject(string key)
    {
        var existing = Child(key);
        if (existing is null)
        {
            return Add(new VdfNode(key));
        }

        existing.Value = null;
        return existing;
    }

    public void SetValue(string key, string value)
    {
        var existing = Child(key);
        if (existing is null)
        {
            Add(new VdfNode(key, value));
        }
        else
        {
            existing.Value = value;
            existing._children.Clear();
        }
    }

    public string Serialize()
    {
        var sb = new StringBuilder();
        Write(sb, 0);
        return sb.ToString();
    }

    private void Write(StringBuilder sb, int depth)
    {
        var indent = new string('\t', depth);
        if (!IsObject)
        {
            sb.Append(indent).Append(Quote(Key)).Append("\t\t").Append(Quote(Value!)).Append('\n');
            return;
        }

        sb.Append(indent).Append(Quote(Key)).Append('\n');
        sb.Append(indent).Append("{\n");
        foreach (var c in _children)
        {
            c.Write(sb, depth + 1);
        }

        sb.Append(indent).Append("}\n");
    }

    internal static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
}
