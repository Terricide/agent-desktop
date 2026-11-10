using System.Runtime.InteropServices;
using System.Threading;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// Joins COM to the multithreaded apartment and applies per-monitor-v2 DPI awareness. The owned-process
/// bootstrap is for the CLI, which calls it once from its main thread. The hosted-library bootstrap is for
/// library hosts whose threads this product does not own: <c>CoIncrementMTAUsage</c> acts on the whole
/// process, so its guard is process-wide and its cookie is retained for the life of the process.
/// </summary>
internal static class ComRuntime
{
    private const int SFalseHresult = 1;
    private const int RpcEChangedModeHresult = unchecked((int)0x80010106);
    private const int AptTypeMtaValue = 1;
    private const uint CoinitMultithreaded = 0;

    private static readonly Lazy<ComApartment> OwnedProcessBootstrap = new(InitializeOwnedProcessApartment);
    private static readonly Lazy<IntPtr> HostedLibraryBootstrap = new(InitializeHostedLibraryApartment);

    internal static int OwnedInitializations;
    internal static int HostedInitializations;

    /// <summary>
    /// Joins the calling thread to the COM multithreaded apartment and applies per-monitor-v2 DPI awareness.
    /// <c>CoInitializeEx</c> is thread-local, so the process-wide guard is sound only because the CLI calls
    /// this once from its main thread before any COM work. <c>RPC_E_CHANGED_MODE</c> means another component
    /// already chose this thread's apartment mode: the apartment is borrowed, the bootstrap succeeds, and no
    /// <c>CoUninitialize</c> is ever scheduled for it.
    /// </summary>
    internal static void EnsureOwnedProcessMtaAndDpi()
    {
        _ = OwnedProcessBootstrap.Value;
    }

    /// <summary>
    /// Registers process-wide MTA usage and applies per-monitor-v2 DPI awareness for library hosts whose threads
    /// this product does not own. Sound from any host thread, including an STA host's.
    /// </summary>
    internal static void EnsureHostedLibraryMtaAndDpi()
    {
        _ = HostedLibraryBootstrap.Value;
    }

    /// <summary>
    /// Reports whether a newly started thread that never called <c>CoInitializeEx</c> observes membership in
    /// the multithreaded apartment, which becomes true once the process-wide MTA exists. Read-only:
    /// <c>CoGetApartmentType</c> never initializes COM, so probing cannot create the state it reports.
    /// </summary>
    internal static bool IsMtaEstablishedForNewThreads()
    {
        var established = false;
        try
        {
            var probe = new Thread(() => established = ProbeCurrentThreadForMta())
            {
                Name = "agent-desktop-mta-probe",
            };
            probe.Start();
            probe.Join();
        }
        catch (ThreadStartException)
        {
            return false;
        }

        return established;
    }

    internal static bool PermitsCoUninitialize(ComApartment apartment) => apartment == ComApartment.OwnedMta;

    internal static bool TryClassifyCoInitializeHresult(int hresult, out ComApartment apartment)
    {
        switch (hresult)
        {
            case HResult.SOk:
            case SFalseHresult:
                apartment = ComApartment.OwnedMta;
                return true;
            case RpcEChangedModeHresult:
                apartment = ComApartment.BorrowedFromHostMode;
                return true;
            default:
                apartment = default;
                return false;
        }
    }

    internal static bool MtaUsageSucceeded(int hresult) => hresult >= 0;

    internal static bool ApartmentProbeReportsMta(int hresult, int apartmentType) =>
        hresult >= 0 && apartmentType == AptTypeMtaValue;

    private static ComApartment InitializeOwnedProcessApartment()
    {
        Interlocked.Increment(ref OwnedInitializations);
        var hresult = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        if (!TryClassifyCoInitializeHresult(hresult, out var apartment))
        {
            throw ComBootstrapFailure("The COM multithreaded apartment could not be initialized", hresult);
        }

        DpiAwareness.EnsurePerMonitorV2();
        return apartment;
    }

    private static IntPtr InitializeHostedLibraryApartment()
    {
        Interlocked.Increment(ref HostedInitializations);
        var hresult = CoIncrementMTAUsage(out var cookie);
        if (!MtaUsageSucceeded(hresult))
        {
            throw ComBootstrapFailure("Process-wide COM MTA usage could not be registered", hresult);
        }

        DpiAwareness.EnsurePerMonitorV2();
        return cookie;
    }

    private static bool ProbeCurrentThreadForMta()
    {
        var hresult = CoGetApartmentType(out var apartmentType, out _);
        return ApartmentProbeReportsMta(hresult, apartmentType);
    }

    private static AdapterError ComBootstrapFailure(string message, int hresult) =>
        new AdapterError(ErrorCode.Internal, message)
            .WithPlatformDetail(HResult.ComHresultDetail(hresult))
            .WithSuggestion("Verify the host process allows COM initialization, then rerun the command");

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern int CoIncrementMTAUsage(out IntPtr cookie);

    [DllImport("ole32.dll")]
    private static extern int CoGetApartmentType(out int apartmentType, out int qualifier);
}
