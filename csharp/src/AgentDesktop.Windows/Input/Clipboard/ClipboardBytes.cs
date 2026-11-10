using System.Buffers.Binary;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Shared little-endian field readers and clipboard decode and argument errors used by the
/// clipboard wire-format marshallers. Every reader is bounds-checked and refuses a truncated
/// field with an `ACTION_FAILED` error rather than reading past the payload.
/// </summary>
internal static class ClipboardBytes
{
    private const int U16Width = 2;
    private const int U32Width = 4;

    internal static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset, string field) =>
        BinaryPrimitives.ReadUInt16LittleEndian(FieldBytes(bytes, offset, U16Width, field));

    internal static uint ReadU32(ReadOnlySpan<byte> bytes, int offset, string field) =>
        BinaryPrimitives.ReadUInt32LittleEndian(FieldBytes(bytes, offset, U32Width, field));

    /// <summary>
    /// Reads a 32-bit field as a signed value by reinterpreting its bits, so a field stored with
    /// the high bit set decodes to a negative number.
    /// </summary>
    internal static int ReadI32(ReadOnlySpan<byte> bytes, int offset, string field) =>
        unchecked((int)ReadU32(bytes, offset, field));

    /// <summary>
    /// Decodes every complete little-endian UTF-16 unit. A trailing odd byte is not a unit and is
    /// ignored, matching a chunked read that drops an incomplete final chunk.
    /// </summary>
    internal static ushort[] ReadUtf16Units(ReadOnlySpan<byte> bytes)
    {
        var units = new ushort[bytes.Length / U16Width];
        for (var index = 0; index < units.Length; index++)
        {
            units[index] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index * U16Width, U16Width));
        }

        return units;
    }

    internal static AdapterError PayloadError(string message) =>
        new(ErrorCode.ActionFailed, message);

    internal static AdapterError ArgumentError(string message) =>
        AdapterError.InvalidArgs(message);

    /// <summary>
    /// Returns the `width` bytes at `offset`. An offset whose end cannot be represented as an
    /// `int` is reported as an overflow; a field that extends past the payload is reported as
    /// truncated.
    /// </summary>
    private static ReadOnlySpan<byte> FieldBytes(ReadOnlySpan<byte> bytes, int offset, int width, string field)
    {
        if (offset < 0 || offset > int.MaxValue - width)
        {
            throw PayloadError($"{field} offset overflowed");
        }

        if (offset > bytes.Length - width)
        {
            throw PayloadError($"{field} is truncated");
        }

        return bytes.Slice(offset, width);
    }
}
