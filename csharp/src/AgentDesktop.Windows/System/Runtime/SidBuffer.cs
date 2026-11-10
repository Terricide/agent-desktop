using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// An owned copy of a security identifier. The bytes live in a pinned array, so <see cref="Pointer"/> stays
/// valid for as long as this buffer is reachable. A SID is copied in whole 64-bit words, never read through
/// an unaligned byte view.
/// </summary>
internal sealed class SidBuffer
{
    private const int SecurityMaxSidSize = 68;
    private const int WellKnownStorageBytes = 72;

    private readonly ulong[] storage;

    private SidBuffer(ulong[] storage)
    {
        this.storage = storage;
    }

    /// <summary>The address of the copied SID bytes.</summary>
    internal IntPtr Pointer => Marshal.UnsafeAddrOfPinnedArrayElement(storage, 0);

    /// <summary>Copies a SID the operating system reported as valid, bounded by the largest SID the platform permits.</summary>
    internal static SidBuffer CopyFromValid(IntPtr sid) => CopyFromValidWithLimit(sid, SecurityMaxSidSize);

    /// <summary>Copies a valid SID whose length is at most <paramref name="maximumLength"/>.</summary>
    internal static unsafe SidBuffer CopyFromValidWithLimit(IntPtr sid, int maximumLength)
    {
        if (sid == IntPtr.Zero || IsValidSid(sid) == 0)
        {
            throw AdapterError.FromCode(ErrorCode.Internal, "the reported SID is not valid");
        }

        var length = (int)GetLengthSid(sid);
        if (length == 0 || length > maximumLength || length > SecurityMaxSidSize)
        {
            throw AdapterError.FromCode(ErrorCode.Internal, "the reported SID has an impossible length");
        }

        var storage = GC.AllocateArray<ulong>((length + 7) / 8, pinned: true);
        new ReadOnlySpan<byte>((void*)sid, length).CopyTo(MemoryMarshal.AsBytes(storage.AsSpan()));
        return new SidBuffer(storage);
    }

    /// <summary>Builds a well-known SID (such as <c>WinBuiltinAdministratorsSid</c>) and copies it into an owned buffer.</summary>
    internal static SidBuffer WellKnown(int sidType)
    {
        var storage = GC.AllocateArray<ulong>(WellKnownStorageBytes / 8, pinned: true);
        var size = (uint)WellKnownStorageBytes;
        var pointer = Marshal.UnsafeAddrOfPinnedArrayElement(storage, 0);
        if (CreateWellKnownSid(sidType, IntPtr.Zero, pointer, ref size) == 0)
        {
            throw HResult.HresultError("CreateWellKnownSid", HResult.HresultFromWin32((uint)Marshal.GetLastWin32Error()), "build a well-known SID");
        }

        return CopyFromValid(pointer);
    }

    /// <summary>Whether two SIDs name the same principal.</summary>
    internal bool Matches(SidBuffer other) => EqualSid(Pointer, other.Pointer) != 0;

    /// <summary>
    /// The textual <c>S-1-5-...</c> form, used as the per-user path segment under the lock root so two
    /// different token users never share a lock path.
    /// </summary>
    internal string ToStringForm()
    {
        if (ConvertSidToStringSidW(Pointer, out var wide) == 0 || wide == IntPtr.Zero)
        {
            throw HResult.HresultError("ConvertSidToStringSidW", HResult.HresultFromWin32((uint)Marshal.GetLastWin32Error()), "format the SID");
        }

        var text = Marshal.PtrToStringUni(wide) ?? string.Empty;
        LocalFree(wide);
        return text;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int IsValidSid(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern uint GetLengthSid(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int EqualSid(IntPtr first, IntPtr second);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int CreateWellKnownSid(int sidType, IntPtr domainSid, IntPtr sid, ref uint sidSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ConvertSidToStringSidW(IntPtr sid, out IntPtr text);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
