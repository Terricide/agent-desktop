namespace AgentDesktop.Core.Response;

/// <summary>Builds the small JSON objects an error's details carry, as text, with the same
/// writer the envelope uses. No type model is consulted, and no document tree is built.</summary>
public sealed class DetailsBuilder
{
    private readonly EnvelopeWriter _writer = new();

    private readonly List<(string Key, Action<EnvelopeWriter> Value)> _fields = new();

    public DetailsBuilder Add(string key, string value) => Field(key, w => w.String(value));

    public DetailsBuilder Add(string key, bool value) => Field(key, w => w.Bool(value));

    public DetailsBuilder Add(string key, long value) => Field(key, w => w.Number(value));

    public DetailsBuilder AddRaw(string key, string json) => Field(key, w => w.Raw(json));

    public DetailsBuilder AddList(string key, IEnumerable<string> values) => Field(
        key,
        w =>
        {
            w.ArrayStart();
            foreach (var value in values)
            {
                w.Member();
                w.String(value);
            }

            w.ArrayEnd();
        });

    public string Text()
    {
        _writer.ObjectStart();
        foreach (var (key, value) in _fields)
        {
            _writer.Key(key);
            value(_writer);
        }

        _writer.ObjectEnd();
        return _writer.Text();
    }

    private DetailsBuilder Field(string key, Action<EnvelopeWriter> value)
    {
        _fields.Add((key, value));
        return this;
    }
}
