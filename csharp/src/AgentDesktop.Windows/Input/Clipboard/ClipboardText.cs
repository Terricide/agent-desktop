using System.Text;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>Pure `CF_UNICODETEXT` marshalling: NUL-terminated UTF-16LE bytes and `String`.
/// Nothing here opens a clipboard: the payload is decoded and encoded as a byte buffer, so the
/// marshalling is checkable without a live clipboard owner. Both failure kinds are `INVALID_ARGS`
/// and are marked unsent, because a rejected payload and a rejected argument both leave the
/// clipboard untouched. Neither message carries the text itself: only lengths and budgets
/// survive into an error, so a clipboard payload never leaks into the envelope.</summary>
internal static class ClipboardText
{
    /// <summary>UTF-16 units a `CF_UNICODETEXT` resource may hold, in each direction. The cap
    /// bounds one allocation of the payload, so a hostile clipboard resource cannot make this
    /// process allocate without limit.</summary>
    internal const int MaxClipboardTextUtf16 = 1_000_000;

    /// <summary>Decodes a `CF_UNICODETEXT` payload up to its first NUL unit. A payload whose
    /// length is not a whole number of UTF-16 units is a malformed resource, not a truncated
    /// string, so it is refused rather than decoded with a dangling byte. Text after the first
    /// NUL is dropped: `CF_UNICODETEXT` ends at the first NUL. Lone surrogates become the
    /// replacement character, the same lossy form the Rust decoder uses.</summary>
    internal static string DecodeUtf16Text(byte[] bytes)
    {
        if (bytes.Length % 2 != 0)
        {
            throw AdapterError.FromCode(
                ErrorCode.ActionFailed,
                "CF_UNICODETEXT payload length is not a whole number of UTF-16 units");
        }

        var end = FindTerminatorUnit(bytes);
        if (end > MaxClipboardTextUtf16)
        {
            throw AdapterError.FromCode(
                ErrorCode.ActionFailed,
                "CF_UNICODETEXT exceeds the supported resource budget");
        }

        return Encoding.Unicode.GetString(bytes, 0, end * 2);
    }

    /// <summary>Encodes text as a NUL-terminated UTF-16LE payload. An embedded NUL cannot be
    /// represented: the terminator would end the string early, so the caller's text would be
    /// silently truncated on the way in. The unit count is the string length, since a .NET
    /// `String` is already a sequence of UTF-16 units and a surrogate pair counts as two.</summary>
    internal static byte[] EncodeUtf16Text(string text)
    {
        if (text.Contains('\0'))
        {
            throw AdapterError.InvalidArgs("Clipboard text must not contain an embedded NUL");
        }

        var unitCount = text.Length;
        if (unitCount > MaxClipboardTextUtf16)
        {
            throw AdapterError.InvalidArgs(
                "clipboard text exceeds the supported resource budget");
        }

        var payload = new byte[(unitCount + 1) * 2];
        Encoding.Unicode.GetBytes(text, 0, unitCount, payload, 0);
        return payload;
    }

    /// <summary>Index of the UTF-16 unit holding NUL, or the payload length when the payload
    /// carries no terminator. A unit is NUL when both of its little-endian bytes are zero.</summary>
    internal static int FindTerminatorUnit(byte[] bytes)
    {
        var unitCount = bytes.Length / 2;
        for (var unit = 0; unit < unitCount; unit++)
        {
            if (bytes[unit * 2] == 0 && bytes[unit * 2 + 1] == 0)
            {
                return unit;
            }
        }

        return unitCount;
    }
}
