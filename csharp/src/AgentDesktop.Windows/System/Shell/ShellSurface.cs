using global::System.Collections.Generic;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Window;
using static AgentDesktop.Windows.System.Shell.ShellSurfaceKinds;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// Resolves an already-present shell surface to the identity the window observation stack consumes.
/// Reads only; never raises, and therefore takes no interaction policy, which is what makes it callable headless.
/// </summary>
internal static class ShellSurface
{
    /// <summary>
    /// The kind table row for a surface, re-exported from the kind table so the open and close paths read the row
    /// through this resolver.
    /// </summary>
    internal static SurfaceKindRow? RowFor(SnapshotSurface kind)
    {
        return ShellSurfaceKinds.RowFor(kind);
    }

    internal static WindowInfo? ResolveSurface(SnapshotSurface kind, Deadline deadline)
    {
        SurfaceKindRow? row = RowFor(kind);
        if (row is null)
        {
            throw UnknownSurfaceError(kind);
        }

        return ResolveRow(row, deadline);
    }

    internal static WindowInfo? ResolveRow(SurfaceKindRow row, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (!row.ExistsOnBuild)
        {
            throw RefusalError(row);
        }

        return ResolvePresentRow(row);
    }

    internal static AdapterError UnknownSurfaceError(SnapshotSurface kind)
    {
        return new AdapterError(
            ErrorCode.InvalidArgs,
            $"'{Kebab(kind)}' is not a shell surface this adapter resolves")
            .WithSuggestion("Use taskbar, system-tray, system-tray-overflow, start-menu or action-center")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    /// <summary>
    /// The caller-facing answer for a shell surface that is simply not up right now. It is distinct from a build
    /// refusal, which names the build, and from the application-window "window not found", whose recovery guidance
    /// cannot work for a surface no application owns. The suggestion names the command that raises the surface, so
    /// an agent told the surface is closed also learns how to open it.
    /// </summary>
    internal static AdapterError NotOpenError(SnapshotSurface kind)
    {
        return new AdapterError(
            ErrorCode.WindowNotFound,
            $"The '{Kebab(kind)}' shell surface is not open on this desktop")
            .WithSuggestion($"Run 'open-system-surface --surface {Kebab(kind)}' to raise it, then retry")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    /// <summary>
    /// The informative refusal for a kind this build does not expose: a bare "not supported" cannot be told apart
    /// from "not implemented", so the detail names the build and the surface that carries the capability instead.
    /// </summary>
    internal static AdapterError RefusalError(SurfaceKindRow row)
    {
        string holder = row.CapabilityHolder ?? "an equivalent surface";
        return new AdapterError(
            ErrorCode.InvalidArgs,
            $"the running build does not expose the '{Kebab(row.Kind)}' shell surface")
            .WithPlatformDetail(
                $"Windows build {BuildNumber()} has no '{Kebab(row.Kind)}' surface; the '{holder}' surface carries this capability on this build")
            .WithSuggestion($"Use the '{holder}' surface instead")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    internal static string Kebab(SnapshotSurface kind)
    {
        return kind.AsString().Replace('_', '-');
    }

    internal static uint BuildNumber()
    {
        OsVersionInfoW info = new()
        {
            Size = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<OsVersionInfoW>(),
            Csd = new ushort[128],
        };
        if (NativeMethods.RtlGetVersion(ref info) == 0)
        {
            return info.Build;
        }

        return 0;
    }

    /// <summary>
    /// The resolver proper. The immersive liveness predicate is root membership AND an uncloaked read (A26-2): after
    /// dismissal the window survives cloaked with <c>IsWindowVisible</c> still true, and it has left the UIA root, so
    /// either signal alone reads a dismissed surface as open, and the two are read together.
    /// </summary>
    private static WindowInfo? ResolvePresentRow(SurfaceKindRow row)
    {
        switch (row.Family)
        {
            case SurfaceFamily.Win32Class win32:
            {
                nint? handle = ResolveClassChain(win32.Chain);
                return handle is { } found ? WindowInfoFromChainWindow(found) : null;
            }
            case SurfaceFamily.Immersive immersive:
                return ShellSurfaceImmersive.ResolveImmersive(
                    immersive.ExpectedClass,
                    immersive.HostImages,
                    immersive.Landmarks);
            default:
                throw new AdapterError(ErrorCode.Internal, "shell surface row has no known family");
        }
    }

    private static nint? ResolveClassChain(IReadOnlyList<string> chain)
    {
        if (chain.Count == 0)
        {
            return null;
        }

        nint? current = FindWindowByClass(null, chain[0]);
        for (int index = 1; index < chain.Count; index++)
        {
            if (current is not { } parent)
            {
                return null;
            }

            current = FindWindowByClass(parent, chain[index]);
        }

        if (current is not { } handle)
        {
            return null;
        }

        return WindowOps.WindowClassName(handle) == chain[chain.Count - 1] ? handle : null;
    }

    private static nint? FindWindowByClass(nint? parent, string className)
    {
        nint found = parent is { } owner
            ? NativeMethods.FindWindowExW(owner, nint.Zero, className, null)
            : NativeMethods.FindWindowW(className, null);
        return found == nint.Zero ? null : found;
    }

    /// <summary>
    /// Builds the tray-family identity the same way the window listing does. The taskbar family is owned by explorer,
    /// read off the exact window the chain walked to. A window whose owning process cannot be read has no identity to
    /// hand out, so it resolves as absent rather than as a half-identified entry.
    /// </summary>
    private static WindowInfo? WindowInfoFromChainWindow(nint handle)
    {
        if (WindowOps.ProcessFacts(handle) is not { } owner)
        {
            return null;
        }

        return new WindowInfo
        {
            Id = WindowOps.WindowId(handle),
            Title = WindowIdentity.LiveWindowTitle(handle) ?? string.Empty,
            App = owner.App,
            Pid = owner.Pid,
            ProcessInstance = owner.Token,
            Bounds = global::AgentDesktop.Windows.WindowEnum.WindowRect(handle),
            State = WindowOps.BuildWindowState(
                WindowOps.IsForegroundWindow(handle),
                global::AgentDesktop.Windows.Interop.IsIconic(handle) != 0,
                global::AgentDesktop.Windows.Interop.IsWindowVisible(handle) != 0),
        };
    }

    internal static nint? ShellTrayHandle()
    {
        return ResolveClassChain(new[] { "Shell_TrayWnd" });
    }

    /// <summary>
    /// The top-level window a class chain starts at: the half of the chain the close path reads visibility from,
    /// since a chain's descendant toolbar stays materialized after dismissal while its top-level window hides.
    /// </summary>
    internal static nint? ClassChainTopHandle(IReadOnlyList<string> chain)
    {
        return chain.Count == 0 ? null : FindWindowByClass(null, chain[0]);
    }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct OsVersionInfoW
    {
        public uint Size;
        public uint Major;
        public uint Minor;
        public uint Build;
        public uint PlatformId;

        [global::System.Runtime.InteropServices.MarshalAs(
            global::System.Runtime.InteropServices.UnmanagedType.ByValArray,
            SizeConst = 128)]
        public ushort[] Csd;
    }

    private static class NativeMethods
    {
        [global::System.Runtime.InteropServices.DllImport("ntdll.dll", ExactSpelling = true)]
        internal static extern int RtlGetVersion(ref OsVersionInfoW info);

        [global::System.Runtime.InteropServices.DllImport(
            "user32.dll",
            CharSet = global::System.Runtime.InteropServices.CharSet.Unicode,
            EntryPoint = "FindWindowW")]
        internal static extern nint FindWindowW(string className, string? windowName);

        [global::System.Runtime.InteropServices.DllImport(
            "user32.dll",
            CharSet = global::System.Runtime.InteropServices.CharSet.Unicode,
            EntryPoint = "FindWindowExW")]
        internal static extern nint FindWindowExW(nint parent, nint childAfter, string className, string? windowName);
    }
}
