using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Uia;

/// <summary>Acquires a pattern from an element and binds it, the way Rust
/// `get_pattern::<T>()` does. A pattern the element does not expose carries sentinel 0, the
/// code Rust feeds to `classify_write("get_pattern", label, &error)`; a call the provider
/// refused carries its HRESULT. The bound pattern owns the pointer the element returned and
/// releases it once on disposal.</summary>
internal static class PatternBinding
{
    /// <summary>The sentinel Rust reports for a pattern an element does not expose.</summary>
    internal const int SentinelAbsent = 0;

    internal static T FromElement<T>(Element element)
        where T : class
    {
        var contract = PatternContract.For(typeof(T));
        var hr = element.CurrentPattern(contract.Id, out var pointer);
        if (hr < 0)
        {
            throw new UiaException(WriteFailure.FromHresult(hr), "get_pattern", contract.Mechanism);
        }

        if (pointer == IntPtr.Zero)
        {
            throw new UiaException(WriteFailure.FromSentinel(SentinelAbsent), "get_pattern", contract.Mechanism);
        }

        return (T)contract.Create(pointer);
    }
}
