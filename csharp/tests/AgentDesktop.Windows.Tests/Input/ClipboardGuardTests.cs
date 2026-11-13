using System.Runtime.InteropServices;
using System.Text;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Input.Clipboard;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>Cases for the ownership-transfer rule: a block that never transferred to the
/// clipboard is ours, so dispose frees it; a block that transferred is released without
/// freeing. The cases exercise the real `GlobalAlloc` family, which is process memory, and
/// never call `SetClipboardData`, so no live clipboard owner is needed.</summary>
public sealed class ClipboardGuardTests
{
    [Fact]
    public void AnEmptyPayloadIsRefusedBeforeAnyAllocation()
    {
        var error = Assert.Throws<AdapterError>(() => ClipboardGuard.FromBytes([]));

        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
    }

    [Fact]
    public void AHandleThatNeverTransferredIsFreedOnDispose()
    {
        var guard = ClipboardGuard.FromBytes(Encoding.ASCII.GetBytes("payload"));
        Assert.False(guard.WasTransferred);

        var before = ClipboardGuard.FreesOnThisThread;
        guard.Dispose();

        Assert.Equal(before + 1, ClipboardGuard.FreesOnThisThread);
    }

    [Fact]
    public void ATransferredHandleIsReleasedWithoutFreeing()
    {
        var guard = ClipboardGuard.FromBytes(Encoding.ASCII.GetBytes("keep-me"));
        var handle = guard.HandleForTest;

        var before = ClipboardGuard.FreesOnThisThread;
        guard.ReleaseWithoutFreeForTest();

        Assert.Equal(before, ClipboardGuard.FreesOnThisThread);
        Assert.Equal(7, (int)GlobalSize(handle));

        var locked = GlobalLock(handle);
        Assert.NotEqual(IntPtr.Zero, locked);
        var bytes = new byte[7];
        Marshal.Copy(locked, bytes, 0, 7);
        Assert.Equal(Encoding.ASCII.GetBytes("keep-me"), bytes);

        GlobalUnlock(handle);
        GlobalFree(handle);
    }

    [Fact]
    public void DisposeAfterReleaseWithoutFreeFreesNothingTwice()
    {
        var guard = ClipboardGuard.FromBytes(Encoding.ASCII.GetBytes("block"));
        var handle = guard.HandleForTest;

        guard.ReleaseWithoutFreeForTest();
        var before = ClipboardGuard.FreesOnThisThread;
        guard.Dispose();

        Assert.Equal(before, ClipboardGuard.FreesOnThisThread);
        Assert.Equal(5, (int)GlobalSize(handle));

        GlobalFree(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalSize(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}
