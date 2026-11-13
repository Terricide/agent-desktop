using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows;

/// <summary>The live application inventory: the owning processes of the agent-facing window
/// set, the ToolHelp process table they are corroborated against, and the protected-process
/// rule. Every listed app owns at least one agent-facing window and is judged on the process
/// identity the kernel reports, never on a name guessed from a window.</summary>
internal static class AppOps
{
    private static readonly string[] ProtectedProcesses =
    [
        "csrss.exe",
        "wininit.exe",
        "winlogon.exe",
        "services.exe",
        "lsass.exe",
        "smss.exe",
        "lsaiso.exe",
        "dwm.exe",
        "explorer.exe",
    ];

    private const uint Th32csSnapprocess = 0x00000002;

    /// <summary>Exact case-insensitive `.exe` image-name match against the session- and
    /// shell-critical set. Near-misses (`iexplore.exe`, `explorer++.exe`, a name merely
    /// containing `lsass`) are deliberately not protected.</summary>
    internal static bool IsProtectedProcess(string identifier)
    {
        var separator = identifier.LastIndexOfAny(['\\', '/']);
        var image = separator < 0 ? identifier : identifier[(separator + 1)..];
        return ProtectedProcesses.Any(protectedName =>
            string.Equals(image, protectedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A process snapshot row: the image name and the pid it belongs to.
    /// <see cref="ListAppsLive"/> joins the window inventory's owning processes with this
    /// snapshot so the two inventories corroborate each other.</summary>
    internal sealed record ProcessRow(ProcessId Pid, string Name);

    /// <summary>Snapshots every running process's image name from the ToolHelp table.
    /// `CreateToolhelp32Snapshot` reports failure as `INVALID_HANDLE_VALUE` (-1), never as a
    /// zero handle, so only that value is tested; a failed call throws instead of reading as an
    /// empty enumeration.</summary>
    internal static List<ProcessRow> CaptureProcessRows()
    {
        var snapshot = Interop.CreateToolhelp32Snapshot(Th32csSnapprocess, 0);
        if (snapshot == new IntPtr(-1))
        {
            throw new AdapterError(ErrorCode.Internal, "CreateToolhelp32Snapshot failed to enumerate processes");
        }

        var rows = new List<ProcessRow>();
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        var ok = Interop.Process32FirstW(snapshot, ref entry);
        while (ok != 0)
        {
            rows.Add(new ProcessRow(ProcessId.From(entry.ProcessId), entry.Name));
            ok = Interop.Process32NextW(snapshot, ref entry);
        }

        Interop.CloseHandle(snapshot);
        return rows;
    }

    /// <summary>The live `list_apps` inventory.
    ///
    /// Every listed app owns at least one agent-facing window and corroborates its identity
    /// against the process snapshot: a pid present in the window inventory but absent from the
    /// process snapshot fails the inventory rather than emitting a half-identified app. A
    /// window's process token is read again at assembly time and compared, so a process that
    /// changed generation mid-listing also fails the inventory. `bundle_id` has no Windows
    /// analogue and is left unset rather than faked.
    ///
    /// Every listed app is `Foreground`: the filter keeps only visible, uncloaked, non-tool
    /// top-level windows, which is the set the taskbar shows.
    ///
    /// Bounds every native call under `deadline`, refusing before any enumeration when the
    /// budget is already spent and checking it again between the per-window process reads the
    /// assembly loop performs.</summary>
    internal static List<AppInfo> ListAppsLive(Deadline deadline)
    {
        deadline.EnsureRemaining();
        var owners = OwningProcesses(deadline);
        var snapshot = CaptureProcessRows();
        var snapshotByPid = new Dictionary<uint, ProcessRow>();
        foreach (var row in snapshot)
        {
            snapshotByPid[row.Pid.Value] = row;
        }

        var seen = new HashSet<uint>();
        var apps = new List<AppInfo>();
        for (var index = 0; index < owners.Count; index++)
        {
            if (index > 0)
            {
                deadline.EnsureRemaining();
            }

            var owner = ProcessTokenOf(owners[index].Handle);
            if (owner is null)
            {
                continue;
            }

            var (pid, token) = owner.Value;
            if (!seen.Add(pid.Value))
            {
                continue;
            }

            if (!snapshotByPid.TryGetValue(pid.Value, out var row))
            {
                throw new AdapterError(ErrorCode.Internal, "a window-owning process is absent from the process snapshot");
            }

            if (token is not null && ProcessTimes.TokenForPid(pid.Value) != token)
            {
                throw new AdapterError(ErrorCode.Internal, "window and process identities disagree for an app");
            }

            apps.Add(new AppInfo
            {
                Name = row.Name,
                Pid = pid,
                ProcessInstance = token,
                Presentation = AppPresentation.Foreground,
            });
        }

        return apps.OrderBy(app => app.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The owning processes of the agent-facing window set: the "apps with a window"
    /// population `list_apps` reports. Checks the deadline between windows, so the budget bounds
    /// however long the walk is allowed to take before the app assembly loop starts.</summary>
    private static List<EnumeratedWindow> OwningProcesses(Deadline deadline)
    {
        var owners = new List<EnumeratedWindow>();
        AdapterError? failure = null;
        var seen = 0;
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (seen > 0 && deadline.IsExpired())
            {
                failure = deadline.TimeoutError();
                return false;
            }

            seen++;
            if (WindowEnum.PassesFilter(window))
            {
                owners.Add(window);
            }

            return true;
        });

        if (failure is not null)
        {
            throw failure;
        }

        return owners;
    }

    /// <summary>The owner pid and its process token for a window, reusing the identity path the
    /// window census uses. A handle that addresses no window has no owner.</summary>
    private static (ProcessId Pid, string? Token)? ProcessTokenOf(IntPtr handle)
    {
        var owner = WindowEnum.LiveWindowOwner(handle);
        if (owner is null)
        {
            return null;
        }

        return (ProcessId.From(owner.Value), ProcessTimes.TokenForPid(owner.Value));
    }
}
