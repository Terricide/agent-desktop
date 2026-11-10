using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>A movable global memory block (`GlobalAlloc` with `GMEM_MOVEABLE`) that holds clipboard payload bytes.
/// Ownership of the block passes to the system only when the clipboard accepts it. A block the clipboard
/// refuses, or one that is disposed before transfer, is freed here so no clipboard handle leaks.</summary>
internal sealed partial class MoveableMemory : IDisposable
{
    private const uint GmemMoveable = 0x0002;

    private readonly IntPtr handle;
    private bool transferred;

    private MoveableMemory(IntPtr handle)
    {
        this.handle = handle;
    }

    /// <summary>Copies a non-empty payload into a new moveable block. An empty payload is refused as an
    /// invalid argument because a zero-byte clipboard handle is not a valid clipboard value.</summary>
    public static MoveableMemory FromBytes(byte[] payload)
    {
        if (payload.Length == 0)
        {
            throw AdapterError.FromCode(ErrorCode.InvalidArgs, "clipboard payload is empty");
        }

        IntPtr handle = GlobalAlloc(GmemMoveable, (nuint)payload.Length);
        if (handle == IntPtr.Zero)
        {
            throw AdapterError.FromCode(ErrorCode.ActionFailed, "GlobalAlloc could not allocate the clipboard block");
        }

        var memory = new MoveableMemory(handle);
        try
        {
            CopyInto(handle, payload);
            return memory;
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    /// <summary>The raw block handle, valid until the block is transferred or disposed.</summary>
    public IntPtr Handle => handle;

    /// <summary>Hands the block to the clipboard through <paramref name="accept"/>. When the clipboard accepts it,
    /// the block belongs to the system and is no longer freed here. When it refuses, the block stays owned by
    /// this instance and <see cref="Dispose"/> frees it.</summary>
    public bool TryTransfer(Func<IntPtr, bool> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        if (transferred || !accept(handle))
        {
            return false;
        }

        transferred = true;
        return true;
    }

    /// <summary>Frees the block unless the clipboard accepted it through <see cref="TryTransfer"/>.</summary>
    public void Dispose()
    {
        if (transferred || handle == IntPtr.Zero)
        {
            return;
        }

        GlobalFree(handle);
    }

    private static void CopyInto(IntPtr handle, byte[] payload)
    {
        IntPtr locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            throw AdapterError.FromCode(ErrorCode.ActionFailed, "GlobalLock could not lock the clipboard block");
        }

        try
        {
            Marshal.Copy(payload, 0, locked, payload.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr handle);
}
