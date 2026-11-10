using System;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Ownership-transfer guard for <c>GlobalAlloc(GMEM_MOVEABLE)</c> clipboard payloads.
/// A successful <c>SetClipboardData</c> transfers the handle to the system, so the guard
/// releases without freeing. A failed <c>SetClipboardData</c> leaves the handle ours, so
/// disposing the guard frees it.
/// </summary>
internal sealed class ClipboardGuard : IDisposable
{
    private const uint GmemMoveable = 0x0002;

    [ThreadStatic]
    private static int _freesOnThisThread;

    private IntPtr _handle;
    private bool _transferred;

    private ClipboardGuard(IntPtr handle)
    {
        _handle = handle;
        _transferred = false;
    }

    internal static int FreesOnThisThread => _freesOnThisThread;

    /// <summary>
    /// Allocates a moveable global block and copies <paramref name="bytes"/> into it.
    /// Throws <see cref="AdapterError"/> with <see cref="ErrorCode.InvalidArgs"/> for an empty buffer.
    /// </summary>
    internal static ClipboardGuard FromBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new AdapterError(
                ErrorCode.InvalidArgs,
                "Clipboard payload allocation refused an empty buffer");
        }

        IntPtr handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new AdapterError(
                ErrorCode.Internal,
                "GlobalAlloc failed for a clipboard payload")
                .WithPlatformDetail($"GetLastError={Marshal.GetLastWin32Error()}");
        }

        IntPtr locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new AdapterError(
                ErrorCode.Internal,
                "GlobalLock failed for a clipboard payload")
                .WithPlatformDetail($"GetLastError={Marshal.GetLastWin32Error()}");
        }

        Marshal.Copy(bytes, 0, locked, bytes.Length);
        GlobalUnlock(handle);

        return new ClipboardGuard(handle);
    }

    /// <summary>
    /// Hands the block to the clipboard. On failure the block is freed and the error is
    /// thrown with an unverified delivery disposition, because the clipboard was already emptied.
    /// </summary>
    internal void SetClipboardData(uint format)
    {
        IntPtr result = SetClipboardDataNative(format, _handle);
        if (result == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new AdapterError(
                ErrorCode.ActionFailed,
                "SetClipboardData failed after the clipboard was emptied")
                .WithPlatformDetail(FormattableString.Invariant($"format={format} GetLastError={error}"))
                .WithDisposition(Disposition.DeliveredUnverified());
        }

        _transferred = true;
    }

    internal IntPtr HandleForTest => _handle;

    internal bool WasTransferred => _transferred;

    internal void ReleaseWithoutFreeForTest()
    {
        _transferred = true;
    }

    public void Dispose()
    {
        if (_transferred || _handle == IntPtr.Zero)
        {
            return;
        }

        GlobalFree(_handle);
        _handle = IntPtr.Zero;
        _freesOnThisThread++;
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalAlloc", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "GlobalFree", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "SetClipboardData", SetLastError = true)]
    private static extern IntPtr SetClipboardDataNative(uint format, IntPtr handle);
}
