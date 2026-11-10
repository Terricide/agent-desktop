using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Input.Clipboard;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>Cases for the pure `CF_UNICODETEXT` marshalling: round trips, terminator handling,
/// malformed payload rejection, resource budget rejection, embedded NUL rejection. No test opens
/// a clipboard, so the marshalling is checkable without a live clipboard owner.</summary>
public sealed class ClipboardTextTests
{
    private static byte[] Utf16Le(params uint[] units)
    {
        var bytes = new byte[units.Length * 2];
        for (var index = 0; index < units.Length; index++)
        {
            bytes[index * 2] = (byte)(units[index] & 0xFF);
            bytes[index * 2 + 1] = (byte)(units[index] >> 8);
        }

        return bytes;
    }

    [Fact]
    public void TextRoundTripsTable()
    {
        var cases = new[]
        {
            ("", "empty"),
            ("hello", "ascii"),
            ("line\nbreak", "embedded_newline"),
            ("line\r\nbreak", "crlf"),
            ("a\r\nb\r\n", "repeated_and_trailing_crlf"),
            ("emoji \U0001F600", "non_bmp"),
            ("café", "bmp_non_ascii"),
        };

        foreach (var (text, label) in cases)
        {
            var encoded = ClipboardText.EncodeUtf16Text(text);
            Assert.True(
                encoded[^2] == 0 && encoded[^1] == 0,
                $"{label} must end with a UTF-16 NUL");
            Assert.Equal(text, ClipboardText.DecodeUtf16Text(encoded));
        }
    }

    [Fact]
    public void MissingTerminatorDecodesFullBuffer()
    {
        var bytes = Utf16Le('a', 'b', 'c');
        Assert.Equal("abc", ClipboardText.DecodeUtf16Text(bytes));
    }

    [Fact]
    public void TrailingBytesAfterTerminatorAreIgnored()
    {
        var bytes = Utf16Le('h', 'i', 0, 'x', 'y');
        var withTrailing = new byte[bytes.Length + 2];
        bytes.CopyTo(withTrailing, 0);
        withTrailing[bytes.Length] = 0xFF;
        withTrailing[bytes.Length + 1] = 0xFF;
        Assert.Equal("hi", ClipboardText.DecodeUtf16Text(withTrailing));
    }

    [Fact]
    public void UnpairedSurrogatesDoNotPanic()
    {
        var high = ClipboardText.DecodeUtf16Text(Utf16Le(0xD800, '!', 0));
        Assert.Contains("\uFFFD", high);
        Assert.Contains("!", high);

        Assert.Equal("\uFFFD", ClipboardText.DecodeUtf16Text(Utf16Le(0xDC00, 0)));
    }

    [Fact]
    public void OddLengthPayloadIsRejected()
    {
        var error = Assert.Throws<AdapterError>(() => ClipboardText.DecodeUtf16Text([0x61]));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    [Fact]
    public void OversizedDecodeIsRejected()
    {
        var units = new uint[1_000_002];
        for (var index = 0; index < 1_000_001; index++)
        {
            units[index] = 'a';
        }

        units[1_000_001] = 0;
        var error = Assert.Throws<AdapterError>(
            () => ClipboardText.DecodeUtf16Text(Utf16Le(units)));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    [Fact]
    public void OversizedEncodeIsRejected()
    {
        var text = new string('a', 1_000_001);
        var error = Assert.Throws<AdapterError>(() => ClipboardText.EncodeUtf16Text(text));
        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
    }

    [Fact]
    public void EmbeddedNulEncodeIsRejected()
    {
        var error = Assert.Throws<AdapterError>(() => ClipboardText.EncodeUtf16Text("a\0b"));
        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
    }
}
