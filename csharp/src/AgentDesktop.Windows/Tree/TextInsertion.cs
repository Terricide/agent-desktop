using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>Where a text element's insertion point sits on screen, read from its Text pattern: the bounds of the
/// character before the insertion point, or, after a line break or in an empty field, the start of the line it opens.
/// A degenerate range has no bounds of its own, so the caret is placed from its neighbour.
///
/// The caret is placed by reading a UI Automation Text pattern: take the selection, collapse a copy of the document
/// range to its end, then move that copy's start back one character. A character before the caret carries the line
/// box the caret sits at the right edge of; a range that cannot move back is an empty field, so the caret sits at the
/// left edge of the document's first line. When the character before the caret is a line break, the caret starts the
/// next line: below the box of the break when that box is reported, and below the first line, shifted by the number
/// of breaks the document text ends with, when no box is reported for the break.</summary>
internal static class TextInsertion
{
    /// <summary>SEAM: reading a Text pattern, walking its ranges and reading their bounding rectangles needs the UI
    /// Automation Text pattern surface, which this package has no slots for, so no caret can be read. The command
    /// fails closed with PLATFORM_NOT_SUPPORTED. Port of crates/windows/src/tree/text_insertion.rs.</summary>
    internal static Rect? GetInsertionCaret(NativeHandle handle, Deadline deadline)
    {
        _ = handle;
        deadline.EnsureRemaining();
        throw AdapterError.NotSupported("get_insertion_caret");
    }

    /// <summary>The insertion caret of the element with keyboard focus, when it belongs to
    /// <paramref name="process"/>, or to any process when <paramref name="process"/> is null. The focus read is a
    /// Text pattern read, so it fails closed for the same reason as the caret for a handle.
    /// SEAM: the focused element read and its Text pattern surface are not ported.</summary>
    internal static Rect? GetFocusedInsertionCaret(ProcessId? process, Deadline deadline)
    {
        _ = process;
        deadline.EnsureRemaining();
        throw AdapterError.NotSupported("get_focused_insertion_caret");
    }

    /// <summary>The caret box for a line box, positioned at <paramref name="x"/> along it. A thin caret keeps the
    /// line's left edge and is at least ten pixels tall, so a provider reporting a near-zero line height still
    /// yields a caret the operator can see.</summary>
    internal static Rect CaretAt(double x, Rect line) =>
        new Rect(x, line.Y, 2.0, Math.Max(line.Height, 10.0));

    internal static bool IsLineBreak(string text) =>
        text is "\r" or "\n" or "\r\n" or "\u2028" or "\u2029";

    /// <summary>How many lines <paramref name="text"/> ends, so a caret on an empty line, whose break a provider
    /// reports no bounds for, can be placed that many lines below the first: CR LF is one break, and so is a lone
    /// CR, LF or Unicode separator.</summary>
    internal static uint CountLineBreaks(string text)
    {
        var count = 0u;
        var afterCarriageReturn = false;
        foreach (var character in text)
        {
            var continuesCarriageReturn = character == '\n' && afterCarriageReturn;
            if (!continuesCarriageReturn && character is '\r' or '\n' or '\u2028' or '\u2029')
            {
                count++;
            }

            afterCarriageReturn = character == '\r';
        }

        return count;
    }
}
