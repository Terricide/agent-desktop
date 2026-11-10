using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using Microsoft.Win32.SafeHandles;

namespace AgentDesktop.Windows.System.PrivateFile;

/// <summary>
/// Storage locality classification for private-artifact write surfaces.
/// <para>
/// <c>GetFileInformationByHandleEx(FileRemoteProtocolInfo)</c> (class 13) signals a local volume by failing with
/// <c>ERROR_INVALID_PARAMETER</c> (87): measured, zero of six local targets returned data while three of three remote
/// targets did. An out-of-range class returns the same 87, so 87 counts as a locality signal only behind a control
/// call. <c>FileBasicInfo</c> (class 0) must first succeed on the same handle to prove the call plumbing.
/// </para>
/// <para>
/// If the control call fails the verdict is <c>Unknown</c>, and <c>Unknown</c> is refused for private-artifact writes:
/// failing open would stream private data to SMB storage on a redirected profile. Reads are never gated by locality;
/// only write surfaces consult this class.
/// </para>
/// </summary>
internal static class Locality
{
    internal const int FileBasicInfoSize = 40;

    internal const int FileRemoteProtocolInfoSize = 116;

    /// <summary>
    /// The kernel validates the output buffer alignment before dispatching the information class: a 4-aligned
    /// <c>FILE_REMOTE_PROTOCOL_INFO</c> fails with <c>ERROR_NOACCESS</c> (998) instead of reaching the class handler
    /// that returns the measured 87. The probe buffer is therefore 8-byte aligned while the byte count stays 116.
    /// </summary>
    internal const int AlignedRemoteProtocolInfoSize = 120;

    private const int FileBasicInfoClass = 0;

    private const int FileRemoteProtocolInfoClass = 13;

    private const int ErrorInvalidParameter = 87;

    private const int InvalidHandleValue = -1;

    [ThreadStatic]
    private static bool forceControlFailure;

    [ThreadStatic]
    private static bool forceRemoteLocality;

    internal enum SurfaceLocality
    {
        Local,
        Remote,
        Unknown,
    }

    internal static void RequireLocalForPrivateWrite(SafeFileHandle file, string what)
    {
        switch (AssessFileLocality(file))
        {
            case SurfaceLocality.Local:
                return;
            case SurfaceLocality.Remote:
                throw new AdapterError(
                    ErrorCode.PermDenied,
                    $"{what} resides on remote storage; private artifacts must stay on local disk");
            default:
                throw new AdapterError(
                    ErrorCode.PermDenied,
                    $"{what} locality could not be determined; refusing to write private artifacts to it");
        }
    }

    internal static SurfaceLocality AssessFileLocality(SafeFileHandle file)
    {
        bool controlSucceeded = BasicInfoControlSucceeds(ControlProbeHandle(file));
        int? remoteProbe = RemoteProtocolProbeResult(file);
        return ClassifySurfaceLocality(controlSucceeded, remoteProbe);
    }

    /// <summary>
    /// <paramref name="remoteProbeError"/> is <c>null</c> when the remote-protocol probe succeeded, otherwise the
    /// Win32 error code it reported.
    /// </summary>
    internal static SurfaceLocality ClassifySurfaceLocality(bool controlSucceeded, int? remoteProbeError)
    {
        if (!controlSucceeded)
        {
            return SurfaceLocality.Unknown;
        }

        if (remoteProbeError is null)
        {
            return SurfaceLocality.Remote;
        }

        return remoteProbeError.Value == ErrorInvalidParameter ? SurfaceLocality.Local : SurfaceLocality.Unknown;
    }

    internal static bool BasicInfoControlSucceeds(IntPtr handle)
    {
        long[] buffer = GC.AllocateArray<long>(FileBasicInfoSize / sizeof(long), pinned: true);
        bool succeeded = GetFileInformationByHandleEx(
            handle,
            FileBasicInfoClass,
            Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0),
            FileBasicInfoSize);
        GC.KeepAlive(buffer);
        return succeeded;
    }

    /// <summary>
    /// Returns <c>null</c> on success, otherwise the Win32 last error reported by the probe.
    /// </summary>
    internal static int? RemoteProtocolProbe(IntPtr handle)
    {
        long[] buffer = GC.AllocateArray<long>(AlignedRemoteProtocolInfoSize / sizeof(long), pinned: true);
        bool succeeded = GetFileInformationByHandleEx(
            handle,
            FileRemoteProtocolInfoClass,
            Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0),
            FileRemoteProtocolInfoSize);
        int error = succeeded ? 0 : Marshal.GetLastPInvokeError();
        GC.KeepAlive(buffer);
        return succeeded ? null : error;
    }

    /// <summary>
    /// Runs <paramref name="run"/> with the class-0 control probe forced onto an invalid handle, so the
    /// control-failure branch can be exercised. The previous flag value is restored when the run returns or throws.
    /// </summary>
    internal static T WithForcedControlFailure<T>(Func<T> run)
    {
        bool previous = forceControlFailure;
        forceControlFailure = true;
        try
        {
            return run();
        }
        finally
        {
            forceControlFailure = previous;
        }
    }

    /// <summary>
    /// Forces the remote-protocol probe to report success while the class-0 control call still runs for real, so the
    /// classifier yields <c>Remote</c> and the remote-storage refusal branch can be exercised on ordinary local disk.
    /// The previous flag value is restored when the run returns or throws.
    /// </summary>
    internal static T WithForcedRemoteLocality<T>(Func<T> run)
    {
        bool previous = forceRemoteLocality;
        forceRemoteLocality = true;
        try
        {
            return run();
        }
        finally
        {
            forceRemoteLocality = previous;
        }
    }

    private static IntPtr ControlProbeHandle(SafeFileHandle file)
    {
        if (forceControlFailure)
        {
            return new IntPtr(InvalidHandleValue);
        }

        return file.DangerousGetHandle();
    }

    private static int? RemoteProtocolProbeResult(SafeFileHandle file)
    {
        if (forceRemoteLocality)
        {
            return null;
        }

        return RemoteProtocolProbe(file.DangerousGetHandle());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        IntPtr file,
        int informationClass,
        IntPtr information,
        int bufferSize);
}
