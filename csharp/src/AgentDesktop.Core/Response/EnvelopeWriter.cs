using System.Text;
using System.Text.Json;

namespace AgentDesktop.Core.Response;

/// <summary>Appends JSON text field by field, in the order a type declares its fields.
/// Nothing here asks a type model what a type contains: the envelope path writes bytes
/// from code the source generator emitted for each type.</summary>
public sealed class EnvelopeWriter
{
    private readonly StringBuilder _buffer = new();

    private readonly List<bool> _scope = new();

    /// <summary>Opens an object. Its first field carries no comma; every later one does.</summary>
    public void ObjectStart()
    {
        _buffer.Append('{');
        _scope.Add(true);
    }

    public void ObjectEnd()
    {
        _buffer.Append('}');
        _scope.RemoveAt(_scope.Count - 1);
    }

    /// <summary>Opens an array whose members carry no keys.</summary>
    public void ArrayStart()
    {
        _buffer.Append('[');
        _scope.Add(true);
    }

    public void ArrayEnd()
    {
        _buffer.Append(']');
        _scope.RemoveAt(_scope.Count - 1);
    }

    /// <summary>Writes "name": and the comma that separates it from the previous field.</summary>
    public void Key(string name)
    {
        Separator();
        _buffer.Append('"');
        _buffer.Append(name);
        _buffer.Append("\":");
    }

    /// <summary>Writes the comma that separates an array member from the previous one.</summary>
    public void Member()
    {
        Separator();
    }

    public void String(string value)
    {
        _buffer.Append('"');
        _buffer.Append(Encode(value));
        _buffer.Append('"');
    }

    public void Bool(bool value)
    {
        _buffer.Append(value ? "true" : "false");
    }

    public void Number(long value)
    {
        _buffer.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public void Number(double value)
    {
        _buffer.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Appends a document the caller built as JSON text, so a command can carry a
    /// value the envelope types do not model.</summary>
    public void Raw(string json)
    {
        _buffer.Append(json);
    }

    public string Text() => _buffer.ToString();

    private void Separator()
    {
        var index = _scope.Count - 1;
        if (!_scope[index])
        {
            _buffer.Append(',');
        }

        _scope[index] = false;
    }

    private static string Encode(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var encoded = JsonEncodedText.Encode(value);
        return encoded.Value.Length == 0 ? value : encoded.Value;
    }
}
