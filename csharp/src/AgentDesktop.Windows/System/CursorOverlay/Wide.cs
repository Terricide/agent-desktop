using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// A .NET string as the null-terminated UTF-16 buffer the Win32 <c>W</c> entry points read, plus the
/// last-error-to-<see cref="AdapterError"/> conversion every cursor overlay call site otherwise re-derives.
/// </summary>
/// <remarks>
/// The terminator is the part that matters: a <c>W</c> function reads until it finds one, so a buffer without it is
/// read past its end.
/// </remarks>
internal static class Wide
{
    /// <summary>
    /// Returns a fresh UTF-16 buffer for <paramref name="value"/> with a trailing NUL. The caller binds the buffer to
    /// a local before the call it is passed to, so the array outlives the native call.
    /// </summary>
    internal static char[] Terminated(string value)
    {
        var buffer = new char[value.Length + 1];
        value.CopyTo(0, buffer, 0, value.Length);
        buffer[value.Length] = '\0';
        return buffer;
    }

    /// <summary>
    /// Reads the last Win32 error for the failed operation and folds it into an internal <see cref="AdapterError"/>
    /// naming what failed. <paramref name="message"/> describes the operation; the code lands in the platform detail.
    /// </summary>
    /// <remarks>
    /// The value is read through <c>Marshal.GetLastPInvokeError</c>, which is only meaningful when the P/Invoke
    /// declaration sets <c>SetLastError = true</c>. Call it immediately after the failing native call.
    /// </remarks>
    internal static AdapterError Win32Error(string message)
    {
        var code = global::System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        return AdapterError.FromCode(ErrorCode.Internal, message).WithPlatformDetail($"Win32 error {code}");
    }
}
