using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.System.Runtime;
using Microsoft.Win32.SafeHandles;

namespace AgentDesktop.Windows.System.PrivateFile;

/// <summary>
/// The DACL half of the private-file contract. Protecting a directory pins it to the principals this process
/// trusts, and the protected write means an inherited ACE from a parent cannot re-open the path. Checking a
/// file reads the DACL the object actually carries, not the one this process asked for, and refuses every
/// access-allowed grant outside that principal set: an untrusted grant means the path can be read or written
/// by a principal the product does not control, so the write is refused before it lands. An ACE that is not
/// access-allowed, and an ACE size that cannot hold a SID, are refused as untrusted rather than read.
/// </summary>
internal static class Dacl
{
    private const int AclHeaderLength = 8;
    private const int AceFixedLength = 8;
    private const int MinimumSidLength = 8;
    private const byte AccessAllowedAceType = 0;
    private const uint AclRevision = 2;
    private const int AclSizeInformationClass = 2;
    private const uint FileAllAccess = 0x001F01FF;
    private const uint ContainerInheritAce = 0x2;
    private const uint ObjectInheritAce = 0x1;
    private const int SeFileObject = 1;
    private const uint DaclSecurityInformation = 0x4;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;
    private const int WinLocalSystemSid = 22;
    private const int WinBuiltinAdministratorsSid = 26;

    [StructLayout(LayoutKind.Sequential)]
    private struct AclSizeInformation
    {
        public uint AceCount;
        public uint AclBytesInUse;
        public uint AclBytesFree;
    }

    /// <summary>Pins a directory to the trusted principals, with inheritance so descendants carry the same
    /// grants, and the protected write so no inherited ACE widens the directory afterwards.</summary>
    internal static void ProtectDirectory(SafeFileHandle handle) =>
        Protect(handle, ContainerInheritAce | ObjectInheritAce);

    /// <summary>Refuses a private file whose DACL is absent, and one whose DACL grants access to a principal
    /// outside the trusted set. The security descriptor the read allocates is freed once the DACL has been
    /// checked, because the DACL is the only fact this read keeps.</summary>
    internal static void RequirePrivate(SafeFileHandle handle)
    {
        var status = GetSecurityInfo(handle, SeFileObject, DaclSecurityInformation, IntPtr.Zero, IntPtr.Zero, out var dacl, IntPtr.Zero, out var descriptor);
        if (status != 0)
        {
            throw HResult.AdapterErrorFromWin32(status, "the private file security information could not be read");
        }

        try
        {
            if (dacl == IntPtr.Zero)
            {
                throw new AdapterError(ErrorCode.PermDenied, "private file has no DACL");
            }

            GrantsOnly(dacl, Principals());
        }
        finally
        {
            if (descriptor != IntPtr.Zero)
            {
                LocalFree(descriptor);
            }
        }
    }

    private static void Protect(SafeFileHandle handle, uint inheritance)
    {
        var principals = Principals();
        var aclLength = AclHeaderLength + principals.Sum(sid => AceFixedLength + SidLength(sid));
        var storage = GC.AllocateArray<ulong>((aclLength + 7) / 8, pinned: true);
        var acl = Marshal.UnsafeAddrOfPinnedArrayElement(storage, 0);
        if (InitializeAcl(acl, (uint)aclLength, AclRevision) == 0)
        {
            throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastPInvokeError(), "the private file ACL could not be initialized");
        }

        foreach (var sid in principals)
        {
            if (AddAccessAllowedAceEx(acl, AclRevision, inheritance, FileAllAccess, sid.Pointer) == 0)
            {
                throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastPInvokeError(), "the private file ACL could not grant a principal");
            }
        }

        var status = SetSecurityInfo(handle, SeFileObject, DaclSecurityInformation | ProtectedDaclSecurityInformation, IntPtr.Zero, IntPtr.Zero, acl, IntPtr.Zero);
        GC.KeepAlive(storage);
        GC.KeepAlive(principals);
        if (status != 0)
        {
            throw HResult.AdapterErrorFromWin32(status, "the private file DACL could not be set");
        }
    }

    private static List<SidBuffer> Principals()
    {
        var principals = new List<SidBuffer>
        {
            SidBuffer.WellKnown(WinLocalSystemSid),
            SidBuffer.WellKnown(WinBuiltinAdministratorsSid),
            TokenSid.ReadTokenUserSid(TokenSource.CurrentProcess),
        };
        principals.AddRange(Owner.ProcessOwnerEligibleSids());
        return principals;
    }

    private static void GrantsOnly(IntPtr acl, IReadOnlyList<SidBuffer> accepted)
    {
        if (GetAclInformation(acl, out var info, (uint)Marshal.SizeOf<AclSizeInformation>(), AclSizeInformationClass) == 0)
        {
            throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastPInvokeError(), "the private file ACL could not be read");
        }

        for (uint index = 0; index < info.AceCount; index++)
        {
            if (GetAce(acl, index, out var ace) == 0 || ace == IntPtr.Zero)
            {
                throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastPInvokeError(), "the private file ACE could not be read");
            }

            var aceType = Marshal.ReadByte(ace, 0);
            var aceSize = (int)(ushort)Marshal.ReadInt16(ace, 2);
            if (aceType != AccessAllowedAceType || aceSize < AceFixedLength + MinimumSidLength)
            {
                throw UntrustedGrant();
            }

            var sid = SidBuffer.CopyFromValidWithLimit(IntPtr.Add(ace, AceFixedLength), aceSize - AceFixedLength);
            if (!accepted.Any(candidate => candidate.Matches(sid)))
            {
                throw UntrustedGrant();
            }
        }
    }

    private static int SidLength(SidBuffer sid) => (int)GetLengthSid(sid.Pointer);

    private static AdapterError UntrustedGrant() =>
        new(ErrorCode.PermDenied, "private file DACL grants an untrusted principal");

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int InitializeAcl(IntPtr acl, uint aclLength, uint revision);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int AddAccessAllowedAceEx(IntPtr acl, uint aceRevision, uint aceFlags, uint accessMask, IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int GetAclInformation(IntPtr acl, out AclSizeInformation information, uint informationLength, int informationClass);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int GetAce(IntPtr acl, uint index, out IntPtr ace);

    [DllImport("advapi32.dll")]
    private static extern uint GetLengthSid(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation, IntPtr owner, IntPtr group, out IntPtr dacl, IntPtr sacl, out IntPtr descriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
