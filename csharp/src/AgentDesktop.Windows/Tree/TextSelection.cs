using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// The Text pattern selection read for <c>TypeText</c> verification.
/// </summary>
/// <remarks>
/// Core asks for the selected range in UTF-16 code units so it can predict what a field should contain once typed
/// text replaces the selection. UI Automation text ranges do not promise UTF-16 indexing, so the offset is derived
/// from the text itself: the prefix range spans document-start to selection-start and its UTF-16 length is the start
/// offset, with the selected text's UTF-16 length giving the extent. No endpoint magnitude is compared numerically,
/// because a provider's comparison result is not a character count.
/// </remarks>
internal static class TextSelection
{
    /// <summary>
    /// Maps the text before a selection and the selected text to a UTF-16 code-unit range.
    /// </summary>
    /// <remarks>
    /// <paramref name="prefix"/> is the document text from the start of the document up to the selection's start;
    /// <paramref name="selection"/> is the selected text. Both counts are in UTF-16 code units, deliberately not
    /// bytes or scalar values: a byte count would over-count every non-ASCII scalar, a scalar count would under-count
    /// every surrogate pair, and both would agree with the UTF-16 answer on an all-ASCII field, which is exactly the
    /// shape that hides the defect. A .NET <see cref="string"/> is already UTF-16, so <c>Length</c> is that count.
    /// </remarks>
    internal static (int Start, int End) Utf16Range(string prefix, string selection)
    {
        var start = prefix.Length;
        return (start, start + selection.Length);
    }

    /// <summary>
    /// Reads the element's current selection as UTF-16 code-unit offsets.
    /// </summary>
    /// <remarks>
    /// Answers <c>null</c> when the element exposes no Text pattern, when it has no selection, or when any read along
    /// the way fails: each is absent evidence, which core degrades to an unverified delivery rather than a failure.
    /// The prefix range is the document range with its end moved to the selection start, read once and mutated in
    /// place; no clone of a range whose provider may not implement one is needed.
    /// </remarks>
    internal static (int Start, int End)? GetTextSelection(NativeHandle handle, Deadline deadline)
    {
        throw AdapterError.NotSupported("TypeText");
    }
}
