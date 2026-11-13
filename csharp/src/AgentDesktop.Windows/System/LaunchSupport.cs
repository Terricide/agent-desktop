using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Window;
using static AgentDesktop.Windows.AppOps;

namespace AgentDesktop.Windows.System;

/// <summary>
/// The answers a launch reports to the shared contract: what window the launch caused, the refusal
/// for an instance already running, the refusal when more than one instance matches the target, and
/// the delivery disposition each launch failure carries. A launch that starts a process and shows no
/// window is an answer the caller reads, not a failure.
/// </summary>
internal static class LaunchSupport
{
    private const string ChromiumWindowClass = "Chrome_WidgetWin_1";
    private const int FirstPollMs = 50;
    private const int MaxPollMs = 250;

    /// <summary>
    /// What the launch can honestly report. The app name is the window's app when a window appeared,
    /// and the image name the request named when none did. A windowless launch keeps its process
    /// instance so a later command can verify the same generation.
    /// </summary>
    internal static LaunchResult LaunchResult(string id, ProcessId pid, string processInstance, WindowInfo? window)
    {
        return new LaunchResult
        {
            App = window is not null ? window.App : LaunchPath.ImageFileName(id),
            Pid = pid,
            ProcessInstance = processInstance,
            Window = window,
            Cdp = null,
            Renderer = window is null ? null : DetectRendererFromWindow(window),
            Suggestion = null,
        };
    }

    /// <summary>
    /// The window the launch caused, polled inside the launch budget. A process that presents no
    /// window within the budget reports no window; a process instance that changed underneath the
    /// launch is a real answer and propagates.
    /// </summary>
    internal static WindowInfo? ObserveWindow(ProcessId pid, string processInstance, LaunchOptions options, Deadline deadline)
    {
        var pollMs = FirstPollMs;
        while (true)
        {
            var window = ObserveWindowOnce(pid, processInstance, deadline);
            if (window is not null)
            {
                return window;
            }

            if (!ShouldPollAfterFirstObservation(options.TimeoutMs) || deadline.RemainingMs() == 0)
            {
                return null;
            }

            var remainingMs = deadline.RemainingMs();
            Thread.Sleep((int)Math.Max(1, Math.Min(pollMs, remainingMs)));
            pollMs = Math.Min(pollMs * 3 / 2, MaxPollMs);
        }
    }

    /// <summary>
    /// One window observation, tolerant of the inventory's mid-walk identity race. The shared listing
    /// budget absorbs that race inside <see cref="WindowOps.ListWindowsLive"/>, so a window that
    /// survives the whole budget, and a budget that runs out entirely, both say the desktop could not
    /// be read coherently in time rather than that this launch failed. Any other failure propagates.
    /// </summary>
    private static WindowInfo? ObserveWindowOnce(ProcessId pid, string processInstance, Deadline deadline)
    {
        try
        {
            return ExactWindow(pid, processInstance, deadline);
        }
        catch (AdapterError error) when (IsUnobservableWithinBudget(error.Code))
        {
            return null;
        }
        catch (AdapterError error)
        {
            throw AfterLaunch(error);
        }
    }

    /// <summary>
    /// The window owned by this pid, and the process instance the window reports. A window whose
    /// process instance is not the one the launch created names a different generation, which is a
    /// refusal, not a window to hand back.
    /// </summary>
    private static WindowInfo? ExactWindow(ProcessId pid, string processInstance, Deadline deadline)
    {
        var windows = WindowOps.ListWindowsLive(
            new WindowFilter { FocusedOnly = false, App = null },
            deadline);
        var window = windows.FirstOrDefault(candidate => candidate.Pid == pid);
        if (window is null)
        {
            return null;
        }

        if (window.ProcessInstance != processInstance)
        {
            throw new AdapterError(
                ErrorCode.AppUnresponsive,
                "Application process instance changed while waiting for its window")
                .WithDetails($"{{\"pid\":{pid.Value},\"complete\":false}}");
        }

        return window;
    }

    /// <summary>
    /// The renderer a launched window shows, read from its window class. Chromium is the window class
    /// a Chromium, Electron, or CEF runtime draws into. An unreadable handle or a class that is not
    /// Chromium reports undetected, which is not a claim that the app is not Chromium.
    /// </summary>
    private static RendererKind? DetectRendererFromWindow(WindowInfo window)
    {
        var handle = WindowOps.ParseHandle(window.Id);
        if (handle == 0)
        {
            return null;
        }

        return WindowOps.WindowClassName(handle) == ChromiumWindowClass ? RendererKind.Chromium : null;
    }

    /// <summary>
    /// The refusal for a launch that asked for a fresh process while one is already running. It names
    /// the running pid and every candidate, so the caller can attach, close, or pick.
    /// </summary>
    internal static AdapterError AlreadyRunningError(ProcessId pid, IReadOnlyList<ProcessRow> matches)
    {
        var candidates = string.Join(",", matches.Select(row => row.Pid.Value));
        return new AdapterError(ErrorCode.ActionFailed, "Application is already running")
            .WithDetails($"{{\"pid\":{pid.Value},\"candidate_pids\":[{candidates}]}}")
            .WithSuggestion("Omit --no-attach to attach to the running instance, or close it first.");
    }

    /// <summary>
    /// The refusal when more than one running instance matches the launch target. The command selects
    /// a process by name, so it has no flag that picks between them.
    /// </summary>
    internal static AdapterError AmbiguousApps(IReadOnlyList<ProcessRow> matches)
    {
        var pids = matches.Select(row => row.Pid.Value).ToList();
        var candidates = string.Join(",", pids);
        return new AdapterError(
            ErrorCode.AmbiguousTarget,
            "More than one application instance matches the launch target")
            .WithSuggestion(
                $"{pids.Count} running instances match (pids [{candidates}]); this command selects a " +
                "process by name and has no pid or instance flag to disambiguate")
            .WithDetails($"{{\"candidate_pids\":[{candidates}]}}")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    /// <summary>
    /// The disposition of a launch failure before any process started: nothing was delivered.
    /// </summary>
    internal static AdapterError BeforeLaunch(AdapterError error) =>
        error.WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>
    /// The disposition of a launch failure after the process was created: the process exists, so the
    /// effect is delivered and unverified.
    /// </summary>
    internal static AdapterError AfterLaunch(AdapterError error) =>
        error.WithDisposition(DeliverySemantics.DeliveredUnverified());

    private static bool IsUnobservableWithinBudget(ErrorCode code) =>
        code is ErrorCode.WindowNotFound or ErrorCode.Timeout;

    private static bool ShouldPollAfterFirstObservation(ulong timeoutMs) => timeoutMs > 0;
}
