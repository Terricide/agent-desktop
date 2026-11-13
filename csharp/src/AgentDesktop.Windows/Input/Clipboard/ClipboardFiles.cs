using System.Buffers.Binary;
using System.Text;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Pure CF_HDROP marshalling: a DROPFILES header followed by a double-NUL terminated list of
/// UTF-16 paths, converted to and from a list of strings. Reads and writes no clipboard state.
/// </summary>
internal static class ClipboardFiles
{
    private const int MaxHdropPaths = 1_024;
    private const int MaxHdropPathUtf16 = 16_384;
    private const int MaxHdropTotalUtf16 = 1_000_000;

    private const int DropFilesSize = 20;
    private const int PFilesOffset = 0;
    private const int PtXOffset = 4;
    private const int PtYOffset = 8;
    private const int FncOffset = 12;
    private const int FWideOffset = 16;

    public static IReadOnlyList<string> DecodeHdrop(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < DropFilesSize)
        {
            throw PayloadError("CF_HDROP payload is shorter than DROPFILES");
        }

        var pFiles = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(PFilesOffset, 4));
        var fWide = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(FWideOffset, 4));
        if (pFiles < DropFilesSize || pFiles > (uint)bytes.Length)
        {
            throw PayloadError("CF_HDROP pFiles offset is out of range");
        }

        if (fWide == 0)
        {
            throw PayloadError("CF_HDROP ANSI path lists are not supported; expected fWide");
        }

        return DecodeWidePaths(bytes[(int)pFiles..]);
    }

    public static byte[] EncodeHdrop(IReadOnlyList<string> paths)
    {
        if (paths.Count > MaxHdropPaths)
        {
            throw ArgumentError("CF_HDROP path list exceeds the supported entry budget");
        }

        long totalUnits = 0;
        foreach (var path in paths)
        {
            if (path.Contains('\0'))
            {
                throw ArgumentError("CF_HDROP paths must not contain an embedded NUL");
            }

            var units = path.Length;
            if (units > MaxHdropPathUtf16)
            {
                throw ArgumentError("CF_HDROP path exceeds the supported length budget");
            }

            totalUnits += units;
            if (totalUnits > MaxHdropTotalUtf16)
            {
                throw ArgumentError("CF_HDROP paths exceed the total text budget");
            }
        }

        var list = WideListBytes(paths);
        var output = new byte[DropFilesSize + list.Length];
        WriteDropFiles(output.AsSpan(0, DropFilesSize), DropFilesSize);
        list.CopyTo(output, DropFilesSize);
        return output;
    }

    private static IReadOnlyList<string> DecodeWidePaths(ReadOnlySpan<byte> list)
    {
        if (list.Length % 2 != 0)
        {
            throw PayloadError("CF_HDROP wide path list length is not a whole number of UTF-16 units");
        }

        if (list.Length < 4)
        {
            throw PayloadError("CF_HDROP wide path list is missing the double-NUL terminator");
        }

        var units = new ushort[list.Length / 2];
        for (var i = 0; i < units.Length; i++)
        {
            units[i] = BinaryPrimitives.ReadUInt16LittleEndian(list.Slice(i * 2, 2));
        }

        if (units.Length < 2 || units[^1] != 0 || units[^2] != 0)
        {
            throw PayloadError("CF_HDROP wide path list is missing the double-NUL terminator");
        }

        var paths = new List<string>();
        long totalUnits = 0;
        var start = 0;
        var index = 0;
        while (index + 1 < units.Length)
        {
            if (units[index] != 0)
            {
                index++;
                continue;
            }

            if (index == start)
            {
                if (index + 1 < units.Length && units[index + 1] == 0 && start == 0 && paths.Count == 0)
                {
                    return Array.Empty<string>();
                }

                throw PayloadError("CF_HDROP path entry is empty");
            }

            if (paths.Count >= MaxHdropPaths)
            {
                throw PayloadError("CF_HDROP path list exceeds the supported entry budget");
            }

            var length = index - start;
            if (length > MaxHdropPathUtf16)
            {
                throw PayloadError("CF_HDROP path exceeds the supported length budget");
            }

            totalUnits += length;
            if (totalUnits > MaxHdropTotalUtf16)
            {
                throw PayloadError("CF_HDROP paths exceed the total text budget");
            }

            paths.Add(Encoding.Unicode.GetString(list.Slice(start * 2, length * 2)));
            index++;
            start = index;
            if (index < units.Length && units[index] == 0)
            {
                return paths;
            }
        }

        throw PayloadError("CF_HDROP wide path list is missing the double-NUL terminator");
    }

    /// <summary>
    /// Each path is followed by one NUL and the list ends with one more NUL. An empty list still
    /// carries two NUL units, so the terminator is always a double NUL.
    /// </summary>
    private static byte[] WideListBytes(IReadOnlyList<string> paths)
    {
        using var buffer = new MemoryStream();
        foreach (var path in paths)
        {
            var encoded = Encoding.Unicode.GetBytes(path);
            buffer.Write(encoded, 0, encoded.Length);
            buffer.Write(new byte[2], 0, 2);
        }

        buffer.Write(new byte[2], 0, 2);
        if (paths.Count == 0)
        {
            buffer.Write(new byte[2], 0, 2);
        }

        return buffer.ToArray();
    }

    private static void WriteDropFiles(Span<byte> destination, int pFiles)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(PFilesOffset, 4), (uint)pFiles);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(PtXOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(PtYOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(FncOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(FWideOffset, 4), 1);
    }

    private static AdapterError PayloadError(string message) =>
        new(ErrorCode.ActionFailed, message);

    private static AdapterError ArgumentError(string message) =>
        AdapterError.InvalidArgs(message);
}
