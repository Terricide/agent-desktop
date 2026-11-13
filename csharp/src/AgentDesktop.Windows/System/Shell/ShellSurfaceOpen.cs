using global::System;
using global::System.Collections.Generic;
using global::System.Diagnostics;
using global::System.Runtime.InteropServices;
using global::System.Threading;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Window;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// Opens and closes shell surfaces, reporting an open only from an observed, presented resolve and a
/// close only from an observed loss of presentation. Never from the raise itself reporting success.
/// </summary>
/// <remarks>
/// The open refuses before anything is raised when the caller's policy does not permit the foreground to
/// move. Closing on an already-closed surface is a no-op, which keeps entry-and-exit cleanup idempotent.
/// </remarks>
internal static class ShellSurfaceOpen
{
    private const uint GaRoot = 0x0002;

    /// <summary>A landmark-free raise-presented window must persist this long to count as a foreign shape.</summary>
    private const long ForeignShapeSettleMs = 500;

    /// <summary>The shell's own response to an accepted re-raise lands inside one poll, so this is short.</summary>
    private const long RaiseSettleMs = 750;

    /// <summary>How long a surface is given to take the foreground that Escape needs.</summary>
    private const long ForegroundSettleMs = 1500;

    private const long PollIntervalMs = 50;

    private const long PollIntervalCeilingMs = 250;

    /// <summary>
    /// Opens a shell surface: refuses before anything is raised when the caller's policy does not permit the
    /// foreground to move, returns an already-present surface without raising, and otherwise raises and then
    /// waits for the surface to be observed.
    /// </summary>
    internal static WindowInfo OpenSurface(SnapshotSurface kind, InteractionPolicy policy, Deadline deadline)
    {
        SurfaceKindRow? row = ShellSurface.RowFor(kind);
        if (row is null)
        {
            throw ShellSurface.UnknownSurfaceError(kind);
        }

        RequireForegroundPolicy(policy);
        return OpenRow(row, deadline);
    }

    /// <remarks>
    /// SEAM: ShellSurfaceOpen.cs:RequireForegroundPolicy needs the InteractionPolicy allow-focus-steal predicate and
    /// the policy-denied-for-policy error, neither ported to AgentDesktop.Core.Context yet.
    /// </remarks>
    private static void RequireForegroundPolicy(InteractionPolicy policy) =>
        throw AdapterError.NotSupported("open a shell surface");

    /// <summary>
    /// A resolved identity is only an open when the surface is presented: the overflow's window class survives
    /// dismissal (A26-6), so its resolver answers while hidden. The immersive family's liveness already encodes
    /// presented (root membership and an uncloaked read, A26-2).
    /// </summary>
    internal static WindowInfo OpenRow(SurfaceKindRow row, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (!row.ExistsOnBuild)
        {
            throw ShellSurface.RefusalError(row);
        }

        WindowInfo? existing = ShellSurface.ResolveRow(row, deadline);
        if (existing is not null && SurfacePresented(row, deadline))
        {
            return existing;
        }

        IReadOnlyList<nint> preRaiseChildren = row.Family switch
        {
            SurfaceFamily.Immersive => ShellSurfaceImmersive.WitnessImmersiveChildren(),
            _ => [],
        };

        ShellSurfaceRaise.RaiseRow(row, deadline);
        return PollUntilObserved(row, deadline, preRaiseChildren);
    }

    /// <summary>
    /// Every open ends in an observed, presented resolve or a <c>TIMEOUT</c>, polled at a bounded interval that
    /// grows rather than sleeping a fixed guess. A raise that presents a same-class, same-host surface matching
    /// none of the kind's landmarks resolves as the named foreign-shape refusal instead of burning the deadline.
    /// </summary>
    private static WindowInfo PollUntilObserved(SurfaceKindRow row, Deadline deadline, IReadOnlyList<nint> preRaiseChildren)
    {
        long intervalMs = PollIntervalMs;
        long? foreignSince = null;
        while (true)
        {
            if (deadline.RemainingMs() == 0)
            {
                throw TimeoutError(row, "did not open within the deadline", "shell_surface_not_opened");
            }

            deadline.EnsureRemaining();
            WindowInfo? observed = ShellSurface.ResolveRow(row, deadline);
            if (observed is not null && SurfacePresented(row, deadline))
            {
                return observed;
            }

            if (row.Family is SurfaceFamily.Immersive immersive)
            {
                Client client = AutomationClientSeam();
                bool foreign = ShellSurfaceImmersive.RaisePresentedForeignShape(client, preRaiseChildren, immersive.Landmarks);
                if (ForeignShapeSettled(ref foreignSince, foreign, Stopwatch.GetTimestamp()))
                {
                    throw ShellSurfaceImmersive.ForeignShapeError(immersive.Landmarks);
                }
            }

            Thread.Sleep((int)Math.Min(intervalMs, deadline.RemainingMs()));
            intervalMs = Math.Min(intervalMs * 3 / 2, PollIntervalCeilingMs);
        }
    }

    /// <summary>
    /// The shared UI Automation client for the foreign-shape read. Creation is bounded, and a creation
    /// that fails is reported as the HRESULT that prevented it, because the foreign-shape check is the
    /// proof that decides whether the surface settled on a shape that is not its own.
    /// </summary>
    private static Client AutomationClientSeam()
    {
        var hr = Client.Create(out var client);
        if (hr < 0 || client is null)
        {
            throw AdapterError.FromCode(ErrorCode.ActionFailed, $"UI Automation client creation failed with HRESULT 0x{hr:x}");
        }

        return client;
    }

    private static bool ForeignShapeSettled(ref long? since, bool foreignNow, long now)
    {
        if (!foreignNow)
        {
            since = null;
            return false;
        }

        long first = since ?? now;
        since = first;
        return ElapsedMs(first, now) >= ForeignShapeSettleMs;
    }

    /// <summary>
    /// Dismisses a shell surface and returns once it is observed no longer presented. Safe on an already-closed surface.
    /// </summary>
    internal static void CloseSurface(SnapshotSurface kind, Deadline deadline)
    {
        SurfaceKindRow? row = ShellSurface.RowFor(kind);
        if (row is null)
        {
            throw ShellSurface.UnknownSurfaceError(kind);
        }

        CloseRow(row, deadline);
    }

    internal static void CloseRow(SurfaceKindRow row, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (!SurfacePresented(row, deadline))
        {
            return;
        }

        switch (row.Dismiss)
        {
            case SurfaceDismiss.None:
                return;
            case SurfaceDismiss.Escape:
                AwaitForeground(row, deadline);
                ShellSurfaceRaise.SendChord([], ShellSurfaceKinds.VkEscape, deadline);
                PollUntilGone(row, deadline);
                return;
            case SurfaceDismiss.RaiseThenEscape:
                RaiseThenEscape(row, deadline);
                return;
            case SurfaceDismiss.Toggle:
                if (row.Raise is SurfaceRaise.Accelerator accelerator)
                {
                    ShellSurfaceRaise.SendChord(accelerator.Modifiers, accelerator.Key, deadline);
                }

                PollUntilGone(row, deadline);
                return;
        }
    }

    /// <summary>
    /// Escape is delivered to whichever window owns the foreground, so it is only sent once the surface owns it.
    /// Re-running the shell's own raise either toggles a presented surface closed or activates a visible one, so
    /// the Escape that follows reaches the surface rather than a bystander. A surface that never takes the
    /// foreground is refused by name instead of burning the deadline.
    /// </summary>
    private static void RaiseThenEscape(SurfaceKindRow row, Deadline deadline)
    {
        ShellSurfaceRaise.RaiseRow(row, deadline);
        if (PollWithCap(RaiseSettleMs, deadline, () => !SurfacePresented(row, deadline)))
        {
            return;
        }

        AwaitForeground(row, deadline);
        ShellSurfaceRaise.SendChord([], ShellSurfaceKinds.VkEscape, deadline);
        PollUntilGone(row, deadline);
    }

    private static void AwaitForeground(SurfaceKindRow row, Deadline deadline)
    {
        if (!PollWithCap(ForegroundSettleMs, deadline, () => SurfaceOwnsForeground(row, deadline)))
        {
            throw ForegroundDeclinedError(row);
        }
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it answers true, the deadline expires, or <paramref name="capMs"/>
    /// of settle time passes, whichever is first.
    /// </summary>
    private static bool PollWithCap(long capMs, Deadline deadline, Func<bool> condition)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            deadline.EnsureRemaining();
            if (condition())
            {
                return true;
            }

            long left = Math.Min(Math.Max(0, capMs - ElapsedMs(start, Stopwatch.GetTimestamp())), deadline.RemainingMs());
            if (left == 0)
            {
                return false;
            }

            Thread.Sleep((int)Math.Min(left, PollIntervalMs));
        }
    }

    /// <summary>
    /// Whether a keystroke synthesized now would land in this surface. Ownership is read at the foreground
    /// window's root, because the foreground can be a descendant of the surface (A26-9).
    /// </summary>
    private static bool SurfaceOwnsForeground(SurfaceKindRow row, Deadline deadline)
    {
        nint foreground = Interop.GetForegroundWindow();
        if (foreground == 0)
        {
            return false;
        }

        nint root = GetAncestor(foreground, GaRoot);
        nint? top = SurfaceTopHandle(row, deadline);
        if (top is not null && top.Value == root)
        {
            return true;
        }

        return row.Family switch
        {
            SurfaceFamily.Immersive immersive =>
                ShellSurfaceImmersive.WindowIsSurface(root, immersive.HostImages, immersive.Landmarks),
            _ => false,
        };
    }

    private static nint? SurfaceTopHandle(SurfaceKindRow row, Deadline deadline)
    {
        if (row.Family is SurfaceFamily.Win32Class chain)
        {
            return ShellSurface.ClassChainTopHandle(chain.Chain);
        }

        WindowInfo? info = ShellSurface.ResolveRow(row, deadline);
        if (info is null)
        {
            return null;
        }

        nint handle = WindowOps.ParseHandle(info.Id);
        return handle == 0 ? null : handle;
    }

    /// <summary>
    /// The answer for a surface that is up but unreachable by keystroke. It is not a timeout: waiting longer
    /// cannot change it, because the surface never took the foreground the dismissal needs.
    /// </summary>
    private static AdapterError ForegroundDeclinedError(SurfaceKindRow row) =>
        new AdapterError(
            ErrorCode.ActionFailed,
            $"the '{ShellSurface.Kebab(row.Kind)}' shell surface stayed up without taking the foreground, "
            + "so a dismissal keystroke cannot reach it")
            .WithSuggestion("Activate the surface - click it, or raise it again from the shell - then retry the close")
            .WithDetails("{\"kind\":\"shell_surface_declined_foreground\",\"retryable\":true}")
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>
    /// Whether the surface is presented right now. The overflow's presented state is its top-level window's
    /// visibility (A26-6); the immersive family's is the resolver's root-membership-and-cloak predicate (A26-2).
    /// </summary>
    private static bool SurfacePresented(SurfaceKindRow row, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (row.Family is SurfaceFamily.Win32Class chain)
        {
            nint? top = ShellSurface.ClassChainTopHandle(chain.Chain);
            return top is not null && Interop.IsWindowVisible(top.Value) != 0;
        }

        return ShellSurface.ResolveRow(row, deadline) is not null;
    }

    private static void PollUntilGone(SurfaceKindRow row, Deadline deadline)
    {
        long intervalMs = PollIntervalMs;
        while (true)
        {
            if (deadline.RemainingMs() == 0)
            {
                throw TimeoutError(row, "did not close within the deadline", "shell_surface_not_closed");
            }

            deadline.EnsureRemaining();
            if (!SurfacePresented(row, deadline))
            {
                return;
            }

            Thread.Sleep((int)Math.Min(intervalMs, deadline.RemainingMs()));
            intervalMs = Math.Min(intervalMs * 3 / 2, PollIntervalCeilingMs);
        }
    }

    private static long ElapsedMs(long from, long to) => (to - from) * 1000 / Stopwatch.Frequency;

    private static AdapterError TimeoutError(SurfaceKindRow row, string outcome, string kind) =>
        AdapterError.Timeout($"the '{ShellSurface.Kebab(row.Kind)}' shell surface {outcome}")
            .WithSuggestion(
                "Retry: the shell can decline an accelerator or an invoke without reporting it; "
                + "a build that lacks the surface refuses instead of timing out")
            .WithDetails($"{{\"kind\":\"{kind}\",\"retryable\":true}}")
            .WithDisposition(DeliverySemantics.NotDelivered());

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);
}
