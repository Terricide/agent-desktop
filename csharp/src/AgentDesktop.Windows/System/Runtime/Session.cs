using System.Runtime.InteropServices;
using System.Threading;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// Session-scoped COM apartment lifetime for persistent Windows hosts. It owns one <c>CoIncrementMTAUsage</c>
/// cookie acquired for this session, separate from any process-lifetime cookie the hosted-library bootstrap keeps.
/// COM permits <c>CoDecrementMTAUsage</c> on that cookie from a different thread than the one that acquired it,
/// so releasing from <see cref="Dispose"/> or the finalizer on whatever thread runs it is sound. The cookie is
/// released exactly once: <see cref="Close"/>, <see cref="Dispose"/> and the finalizer share one claim, and only
/// the first to claim it calls the native release.
/// </summary>
internal sealed class WindowsAdapterSession : IDisposable
{
    private const string AcquireFailureMessage = "Session-scoped COM MTA usage could not be registered";
    private const string ReleaseFailureMessage = "Session-scoped COM MTA usage could not be released";
    private const string AcquireFailureSuggestion =
        "Verify the host process allows COM initialization, then reopen the session";
    private const string ReleaseFailureSuggestion =
        "Treat the session as closed; the process retains one leaked MTA usage until exit";

    private readonly IntPtr _cookie;
    private readonly Func<IntPtr, int> _release;
    private int _released;

    private WindowsAdapterSession(IntPtr cookie, Func<IntPtr, int> release)
    {
        _cookie = cookie;
        _release = release;
    }

    /// <summary>Acquires a session-scoped MTA usage registration, failing with <c>TIMEOUT</c> when the deadline is spent.</summary>
    internal static WindowsAdapterSession Open(Deadline deadline) =>
        OpenWith(deadline, AcquireNative, ReleaseNative);

    internal static WindowsAdapterSession OpenWith(
        Deadline deadline,
        Func<(int HResult, IntPtr Cookie)> acquire,
        Func<IntPtr, int> release)
    {
        deadline.EnsureRemaining();
        var (hresult, cookie) = acquire();
        if (!Succeeded(hresult))
        {
            throw AcquireFailure(hresult);
        }

        return new WindowsAdapterSession(cookie, release);
    }

    /// <summary>Releases the cookie and reports a failed release as an <c>INTERNAL</c> error carrying its HRESULT.</summary>
    internal void Close()
    {
        if (ReleaseOnce() is { } hresult && !Succeeded(hresult))
        {
            throw ReleaseFailure(hresult);
        }
    }

    public void Dispose()
    {
        ReleaseOnce();
        GC.SuppressFinalize(this);
    }

    ~WindowsAdapterSession()
    {
        ReleaseOnce();
    }

    /// <summary>Returns the native release's HRESULT for the one call that claims the cookie, or <c>null</c> when it was already released.</summary>
    private int? ReleaseOnce()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return null;
        }

        return _release(_cookie);
    }

    /// <summary>
    /// COM's <c>SUCCEEDED</c> test. Stands in for the unported <c>classify_mta_usage_hresult</c> until its port lands.
    /// </summary>
    private static bool Succeeded(int hresult) => hresult >= HResult.SOk;

    private static AdapterError AcquireFailure(int hresult) =>
        new AdapterError(ErrorCode.Internal, AcquireFailureMessage)
            .WithPlatformDetail(HResult.ComHresultDetail(hresult))
            .WithSuggestion(AcquireFailureSuggestion);

    private static AdapterError ReleaseFailure(int hresult) =>
        new AdapterError(ErrorCode.Internal, ReleaseFailureMessage)
            .WithPlatformDetail(HResult.ComHresultDetail(hresult))
            .WithSuggestion(ReleaseFailureSuggestion);

    private static (int HResult, IntPtr Cookie) AcquireNative()
    {
        var hresult = CoIncrementMTAUsage(out var cookie);
        return (hresult, cookie);
    }

    private static int ReleaseNative(IntPtr cookie) => CoDecrementMTAUsage(cookie);

    [DllImport("ole32.dll")]
    private static extern int CoIncrementMTAUsage(out IntPtr cookie);

    [DllImport("ole32.dll")]
    private static extern int CoDecrementMTAUsage(IntPtr cookie);
}
