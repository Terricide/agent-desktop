using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using global::System.Collections.Generic;

namespace AgentDesktop.Windows.System.Menu;

/// <summary>
/// Whether a menu is open right now for a process, composed from three sources. Bare Menu-family
/// reachability is constant-true at idle (A23-1, A23-2), so every source is a compound predicate
/// that round-trips false across a real open and close. The framework gate in the Chromium arm
/// keeps a Win32 menu bar from firing at rest. Port of crates/windows/src/system/menu/menu_state.rs.
/// </summary>
internal static partial class MenuState
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInvalidParameter = 87;

    internal static bool MenuIsOpen(ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        EnsureProcessExists(pid);
        deadline.EnsureRemaining();
        if (ClassicMenuModeActive(pid, deadline))
        {
            return true;
        }
        deadline.EnsureRemaining();
        if (UiaMenuReachable(pid, deadline))
        {
            return true;
        }
        deadline.EnsureRemaining();
        return MenuStateChromium.ChromiumDomMenuReachable(pid, deadline);
    }

    /// <summary>
    /// A pid that does not exist must not read as a false "no menu open". Neither the thread source
    /// nor the candidate source fails on a nonexistent pid, so existence is checked explicitly.
    /// ERROR_ACCESS_DENIED means the process is alive but its handle could not be opened; neither
    /// source needs that handle, so the check proceeds.
    /// </summary>
    internal static void EnsureProcessExists(ProcessId pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid.Value);
        if (handle != nint.Zero)
        {
            CloseHandle(handle);
            return;
        }
        if (Marshal.GetLastPInvokeError() == ErrorInvalidParameter)
        {
            throw ProcessNotFoundError(pid);
        }
    }

    internal static AdapterError ProcessNotFoundError(ProcessId pid)
    {
        return new AdapterError(
            ErrorCode.AppUnresponsive,
            "The target process could not be found")
            .WithDetails(new JsonObject
            {
                ["kind"] = "process_not_found",
                ["pid"] = pid.Value,
            });
    }

    internal static AdapterError UnresponsiveMenuProbeError()
    {
        return new AdapterError(
            ErrorCode.AppUnresponsive,
            "A candidate menu window is not processing messages, so a UI Automation read would block")
            .WithDetails(new JsonObject { ["kind"] = "menu_probe_window_not_pumping" });
    }

    /// <summary>
    /// Source A (A23-1): the thread snapshot filtered to the pid, with the GUI thread menu-mode flags
    /// read per thread. A pid that may not be foreground needs each of its threads read individually.
    /// Port of <c>classic_menu_mode_active</c>.
    /// </summary>
    internal static bool ClassicMenuModeActive(ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var target = pid.Value;
        var snapshot = Interop.CreateToolhelp32Snapshot(Th32csSnapThread, 0);
        if (snapshot == InvalidHandle)
        {
            throw AdapterError.Internal(
                "CreateToolhelp32Snapshot failed to enumerate threads for the menu-mode read");
        }

        try
        {
            var entry = new byte[ThreadEntrySize];
            BitConverter.GetBytes((uint)entry.Length).CopyTo(entry, 0);
            var first = true;
            for (int ok = Thread32First(snapshot, entry); ok != 0; ok = Thread32Next(snapshot, entry))
            {
                if (!first)
                {
                    deadline.EnsureRemaining();
                }

                first = false;
                if (ReadUInt(entry, OwnerProcessOffset) == target
                    && ThreadReportsMenuMode(ReadUInt(entry, ThreadIdOffset)))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Interop.CloseHandle(snapshot);
        }
    }

    /// <summary>A thread that exited between the snapshot and this read is skipped, not an error:
    /// <c>GetGUIThreadInfo</c> failing is the same honest absence-of-menu-mode signal for a gone thread
    /// as for a thread with no GUI state at all (A23-10 measured a bare console process failing every
    /// thread's read outright, never a zeroed success).</summary>
    private static bool ThreadReportsMenuMode(uint threadId)
    {
        var info = new byte[8 + HandleCount * IntPtr.Size + CaretRectBytes];
        BitConverter.GetBytes((uint)info.Length).CopyTo(info, 0);
        if (GetGUIThreadInfo(threadId, info) == 0)
        {
            return false;
        }

        var flags = BitConverter.ToUInt32(info, FlagsOffset);
        return (flags & MenuModeBits) != 0;
    }

    private static uint ReadUInt(byte[] buffer, int offset) =>
        BitConverter.ToUInt32(buffer, offset);

    private const uint Th32csSnapThread = 0x00000002;
    private const int ThreadEntrySize = 28;
    private const int ThreadIdOffset = 8;
    private const int OwnerProcessOffset = 12;
    private const int FlagsOffset = 4;
    private const uint GuiInMenuMode = 0x4;
    private const uint GuiSystemMenuMode = 0x8;
    private const uint GuiPopupMenuMode = 0x10;
    private const uint MenuModeBits = GuiInMenuMode | GuiSystemMenuMode | GuiPopupMenuMode;
    private const int HandleCount = 6;
    private const int CaretRectBytes = 16;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll")]
    private static extern int Thread32First(IntPtr snapshot, byte[] entry);

    [DllImport("kernel32.dll")]
    private static extern int Thread32Next(IntPtr snapshot, byte[] entry);

    [DllImport("user32.dll")]
    private static extern int GetGUIThreadInfo(uint threadId, byte[] info);

    /// <summary>
    /// Source B (A23-11): every tool window owned by the pid is probed for a reachable
    /// Menu, MenuBar or MenuItem descendant. Tool windows are excluded from the agent-facing window
    /// list, so this predicate cannot fabricate a window-opened event.
    /// </summary>
    internal static bool UiaMenuReachable(ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var candidates = ToolWindowCandidates(pid);
        if (candidates.Count == 0)
        {
            return false;
        }
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0)
            {
                deadline.EnsureRemaining();
            }
            if (ProbeCandidate(candidates[index]))
            {
                return true;
            }
        }
        return false;
    }

    internal static List<nint> ToolWindowCandidates(ProcessId pid)
    {
        var handles = new List<nint>();
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (WindowEnum.LiveWindowOwner(window.Handle) == pid.Value && window.Tool)
            {
                handles.Add(window.Handle);
            }
            return true;
        });
        return handles;
    }

    /// <summary>
    /// Gated on window responsiveness before any cross-process UI Automation call: a target that
    /// stopped dispatching would otherwise block a read with no backstop at any layer. The subtree is
    /// walked on the raw view walker and matched on control type, because the C# UIA layer has no
    /// property-condition binding to build the menu-family OR condition Rust builds. The root is
    /// included, matching Rust's <c>TreeScope::Subtree</c>.
    /// </summary>
    internal static bool ProbeCandidate(nint handle) => ProbeCandidateElement(handle, IsMenuFamily) is not null;

    /// <summary>
    /// <see cref="ProbeCandidate"/> with the found element kept instead of dropped. The probe already located the
    /// menu-family element on its positive answer; the <c>Menu</c> surface arm needs exactly that element to root the
    /// observation at, so the discard was the only difference between the two callers. The caller owns the returned
    /// element.
    /// </summary>
    internal static Element? ProbeCandidateElement(nint handle, Func<Element, bool> matches)
    {
        if (!WindowExists(handle))
        {
            return null;
        }

        if (!WindowEnum.WindowIsResponsive(handle))
        {
            throw UnresponsiveMenuProbeError();
        }

        if (Client.Create(out var client) < 0 || client is null)
        {
            return null;
        }

        try
        {
            if (client.ElementFromHandle(handle, out var rootPointer) < 0 || rootPointer == IntPtr.Zero)
            {
                return null;
            }

            if (client.RawViewWalker(out var walker) < 0 || walker == IntPtr.Zero)
            {
                Calls.Release(rootPointer);
                return null;
            }

            try
            {
                var visited = 0;
                var found = MenuFamilyElementAt(walker, new Element(rootPointer), 0, ref visited, matches);
                if (found is null)
                {
                    Calls.Release(rootPointer);
                }

                return found;
            }
            finally
            {
                Calls.Release(walker);
            }
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>Depth-first over the subtree, bounded so a dense provider cannot run the poll past its
    /// budget. A node that cannot be read is skipped, not reported: the probe answers presence, and a
    /// provider that refuses a read has not shown a menu.</summary>
    private static Element? MenuFamilyElementAt(
        IntPtr walker, Element element, int depth, ref int visited, Func<Element, bool> matches)
    {
        if (visited >= MaxProbeNodes || depth > MaxProbeDepth)
        {
            return null;
        }

        visited++;
        if (matches(element))
        {
            return element;
        }

        if (Calls.CallOutPointerWithArg(walker, Slots.WalkerGetFirstChild, element.Pointer, out var child) < 0)
        {
            return null;
        }

        for (var current = child; current != IntPtr.Zero; )
        {
            var nextHr = Calls.CallOutPointerWithArg(
                walker, Slots.WalkerGetNextSibling, current, out var next);
            var found = MenuFamilyElementAt(walker, new Element(current), depth + 1, ref visited, matches);
            if (found is not null)
            {
                Calls.Release(next);
                return found;
            }

            if (nextHr < 0)
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    internal static bool IsMenuFamily(Element element)
    {
        var kind = element.CurrentControlType();
        return kind.IsKnown()
            && (kind.Value == ControlTypeIds.Menu
                || kind.Value == ControlTypeIds.MenuBar
                || kind.Value == ControlTypeIds.MenuItem);
    }

    private static bool WindowExists(nint handle) =>
        global::System.OperatingSystem.IsWindows() && IsWindow(handle) != 0;

    private const int MaxProbeNodes = 512;
    private const int MaxProbeDepth = 12;

    [DllImport("user32.dll")]
    private static extern int IsWindow(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
