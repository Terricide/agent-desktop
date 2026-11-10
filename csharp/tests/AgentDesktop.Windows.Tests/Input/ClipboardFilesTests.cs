using System.Buffers.Binary;
using System.Text;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Input.Clipboard;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>Verifies CF_HDROP marshalling: DROPFILES header layout, double-NUL path lists, and the
/// rejection of malformed, ANSI, embedded-NUL and over-budget payloads.</summary>
public sealed class ClipboardFilesTests
{
    private const int DropFilesSize = 20;

    private static byte[] Header(uint pFiles, int fWide)
    {
        var header = new byte[DropFilesSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), pFiles);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), fWide);
        return header;
    }

    private static byte[] WideHeaderPayload(byte[] list) =>
        Header((uint)DropFilesSize, 1).Concat(list).ToArray();

    private static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text);

    private static ushort[] ToUnits(byte[] bytes)
    {
        var units = new ushort[bytes.Length / 2];
        for (var i = 0; i < units.Length; i++)
        {
            units[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
        }

        return units;
    }

    private static int FirstDoubleNul(ushort[] units)
    {
        for (var i = 0; i + 1 < units.Length; i++)
        {
            if (units[i] == 0 && units[i + 1] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void HdropRoundTripsEmptySingleAndMultiple()
    {
        var cases = new (IReadOnlyList<string> Paths, string Label)[]
        {
            (Array.Empty<string>(), "empty"),
            (new[] { "C:\\only.txt" }, "single"),
            (new[] { "C:\\a.txt", "C:\\b dir\\c.txt", "D:\\third" }, "multiple"),
        };

        foreach (var (paths, label) in cases)
        {
            var encoded = ClipboardFiles.EncodeHdrop(paths);

            Assert.Equal((uint)DropFilesSize, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0, 4)));
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(16, 4)));

            var list = encoded[DropFilesSize..];
            Assert.True(list.Length >= 4 && list[^4..].SequenceEqual(new byte[] { 0, 0, 0, 0 }),
                $"{label} must end with a wide double-NUL");
            Assert.Equal(0, list.Length % 2);

            var units = ToUnits(list);
            Assert.Equal(units.Length - 2, FirstDoubleNul(units));

            var decoded = ClipboardFiles.DecodeHdrop(encoded);
            Assert.True(paths.SequenceEqual(decoded), label);
        }

        var independentEmpty = Header((uint)DropFilesSize, 1).Concat(new byte[4]).ToArray();
        var decodedEmpty = ClipboardFiles.DecodeHdrop(independentEmpty);
        Assert.Empty(decodedEmpty);
    }

    [Fact]
    public void TruncatedHeaderIsRejected()
    {
        var error = Assert.Throws<AdapterError>(() => ClipboardFiles.DecodeHdrop(new byte[DropFilesSize - 1]));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    [Fact]
    public void TruncatedPathListIsRejected()
    {
        var list = Utf16("C:\\a.txt").Concat(new byte[2]).ToArray();
        var payload = WideHeaderPayload(list);
        var error = Assert.Throws<AdapterError>(() => ClipboardFiles.DecodeHdrop(payload));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    [Fact]
    public void InconsistentPFilesOffsetIsRejected()
    {
        var payload = ClipboardFiles.EncodeHdrop(new[] { "C:\\a.txt" });
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0);
        var error = Assert.Throws<AdapterError>(() => ClipboardFiles.DecodeHdrop(payload));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    [Fact]
    public void AnsiHdropIsRejected()
    {
        var payload = ClipboardFiles.EncodeHdrop(new[] { "C:\\a.txt" });
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), 0);
        var error = Assert.Throws<AdapterError>(() => ClipboardFiles.DecodeHdrop(payload));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    [Fact]
    public void EmbeddedNulPathEncodeIsRejected()
    {
        var error = Assert.Throws<AdapterError>(() => ClipboardFiles.EncodeHdrop(new[] { "C:\\a\0b.txt" }));
        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
    }

    [Fact]
    public void APathListOverTheTotalTextBudgetIsRejectedOnRead()
    {
        const int perPath = 16_000;
        const int pathCount = 100;
        var list = new List<byte>();
        for (var i = 0; i < pathCount; i++)
        {
            for (var j = 0; j < perPath; j++)
            {
                list.Add(0x61);
                list.Add(0);
            }

            list.Add(0);
            list.Add(0);
        }

        list.Add(0);
        list.Add(0);

        var payload = WideHeaderPayload(list.ToArray());
        var error = Assert.Throws<AdapterError>(() => ClipboardFiles.DecodeHdrop(payload));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Contains("total text budget", error.Message);
    }
}
