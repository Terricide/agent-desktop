using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using Microsoft.Win32.SafeHandles;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// Shared process-token and file-owner SID reads. The private-file owner check and the interaction-lease
/// owner check each copy a SID out of its owning call, build a well-known SID, and read a token's owner or
/// user. These mechanics are byte-identical between them, so they live here, while each module keeps its own
/// policy about which SIDs are legitimate. The private-file owner accepts a token's whole owner-eligible set;
/// the interaction lease distinguishes <c>TokenOwner</c> from <c>TokenUser</c> directly.
/// <para>
/// Every token read is the same two-call probe: a zero-length call that reports the required size with
/// <c>ERROR_INSUFFICIENT_BUFFER</c>, then one call that fills it. The buffer is pointer-backed on purpose.
/// <c>TOKEN_USER</c> and <c>TOKEN_OWNER</c> both begin with a pointer to a SID that lives inside the same
/// buffer, so the pointer read must be aligned; a pinned pointer array guarantees that.
/// </para>
/// </summary>
internal static class TokenSid
{
    private const uint TokenQuery = 0x0008;
    private const uint OwnerSecurityInformation = 0x0001;
    private const int SeFileObject = 1;
    private const int TokenUserClass = 1;
    private const int TokenOwnerClass = 2;
    private const uint ErrorInsufficientBuffer = 122;
    private const int PointerSize = 8;

    /// <summary>The user a token names, read through the same two-call probe every other token read here uses.</summary>
    internal static SidBuffer ReadTokenUserSid(TokenSource token) => ReadSidFromToken(token, TokenUserClass);

    /// <summary>The owner a token names. Same probe as the user read, with <c>TokenOwner</c> in place of <c>TokenUser</c>.</summary>
    internal static SidBuffer ReadTokenOwnerSid(TokenSource token) => ReadSidFromToken(token, TokenOwnerClass);

    /// <summary>
    /// The owner <c>GetSecurityInfo</c> reports for an already-open file or directory handle: the read every
    /// owner-eligibility and lease-directory-ownership check starts from. The security descriptor the call
    /// allocates is freed once the owner is copied, because the owner is the only fact this read keeps.
    /// </summary>
    internal static SidBuffer ReadFileOwnerSid(SafeFileHandle file)
    {
        var status = GetSecurityInfo(file, SeFileObject, OwnerSecurityInformation, out var owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
        if (status != 0)
        {
            throw HResult.HresultError("GetSecurityInfo", HResult.HresultFromWin32(status), "read the file owner");
        }

        try
        {
            return SidBuffer.CopyFromValid(owner);
        }
        finally
        {
            if (descriptor != IntPtr.Zero)
            {
                LocalFree(descriptor);
            }
        }
    }

    private static SidBuffer ReadSidFromToken(TokenSource token, int informationClass)
    {
        if (!token.IsCurrentProcess)
        {
            return SidFromInformation(token.Handle, informationClass);
        }

        var own = OpenCurrentProcessToken();
        try
        {
            return SidFromInformation(own, informationClass);
        }
        finally
        {
            CloseHandle(own);
        }
    }

    private static SidBuffer SidFromInformation(IntPtr token, int informationClass)
    {
        var buffer = ReadTokenInformation(token, informationClass);
        var sid = Marshal.ReadIntPtr(Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0));
        var copied = SidBuffer.CopyFromValid(sid);
        GC.KeepAlive(buffer);
        return copied;
    }

    private static IntPtr OpenCurrentProcessToken()
    {
        if (OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token) == 0)
        {
            throw HResult.HresultError("OpenProcessToken", HResult.HresultFromWin32((uint)Marshal.GetLastWin32Error()), "open the process token");
        }

        return token;
    }

    private static IntPtr[] ReadTokenInformation(IntPtr token, int informationClass)
    {
        if (GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out var required) != 0 || required == 0)
        {
            throw AdapterError.FromCode(ErrorCode.Internal, "the process token reported no information payload");
        }

        var probeError = (uint)Marshal.GetLastWin32Error();
        if (probeError != ErrorInsufficientBuffer)
        {
            throw HResult.HresultError("GetTokenInformation", HResult.HresultFromWin32(probeError), "size the token information");
        }

        var buffer = GC.AllocateArray<IntPtr>(((int)required + PointerSize - 1) / PointerSize, pinned: true);
        if (GetTokenInformation(token, informationClass, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), required, out _) == 0)
        {
            throw HResult.HresultError("GetTokenInformation", HResult.HresultFromWin32((uint)Marshal.GetLastWin32Error()), "read the token information");
        }

        return buffer;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint length, out uint required);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation, out IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);
}
