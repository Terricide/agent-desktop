using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows;

/// <summary>The window census: the top-level walk, the four-criterion filter, and the records
/// the shared contract emits. The owner is read from the handle, the generation token from the
/// kernel, and the title from the window's own message queue, so each record cites its own
/// evidence and a window that cannot be named is dropped rather than guessed.</summary>
internal static class WindowCensus
{
    private const uint TitleProbeMs = 100;

    internal static List<WindowInfo> Collect(WindowFilter filter)
    {
        var pass = new CensusPass();
        WindowEnum.EnumerateTopLevel(pass.Record);
        var windows = pass.Windows;
        if (filter.FocusedOnly)
        {
            windows = windows.Where(w => w.State.IsFocused).ToList();
        }

        if (filter.App is not null)
        {
            windows = windows.Where(w => AppInfo.AppNameMatches(w.App, filter.App)).ToList();
        }

        return windows;
    }

    private sealed class CensusPass
    {
        private readonly ProcessSnapshot _snapshot = ProcessSnapshot.Capture();
        private readonly IntPtr _foreground = Interop.GetForegroundWindow();
        private readonly Dictionary<uint, string?> _tokens = new();
        internal readonly List<WindowInfo> Windows = new();

        internal bool Record(EnumeratedWindow window)
        {
            if (!WindowEnum.PassesFilter(window))
            {
                return true;
            }

            var pid = WindowEnum.LiveWindowOwner(window.Handle);
            if (pid is null)
            {
                return true;
            }

            Windows.Add(new WindowInfo
            {
                Id = WindowEnum.WindowId(window.Handle),
                Title = WindowEnum.LiveWindowTitle(window.Handle, TitleProbeMs),
                App = _snapshot.ImageNameFor(pid.Value) ?? "",
                Pid = ProcessId.From(pid.Value),
                ProcessInstance = Token(pid.Value),
                Bounds = window.Rect,
                State = new WindowState
                {
                    IsFocused = window.Handle == _foreground,
                    Minimized = window.Iconic,
                    Visible = window.Visible,
                },
            });

            return true;
        }

        private string? Token(uint pid)
        {
            if (!_tokens.TryGetValue(pid, out var token))
            {
                token = ProcessTimes.TokenForPid(pid);
                _tokens[pid] = token;
            }

            return token;
        }
    }
}

/// <summary>The application census: the same filtered window walk grouped by owning process,
/// so an app is a process that owns at least one window the census filter keeps. A process
/// the ToolHelp table does not name owns no app record, because the shared contract names an
/// app by the image the kernel reported, not by a title.</summary>
internal static class AppCensus
{
    internal static Core.Locator.AppInventory Collect()
    {
        var windows = WindowCensus.Collect(new WindowFilter());
        var byPid = new Dictionary<uint, AppInfo>();
        var order = new List<uint>();
        foreach (var window in windows)
        {
            var pid = window.Pid.Value;
            if (!byPid.TryGetValue(pid, out var app))
            {
                if (window.App.Length == 0)
                {
                    continue;
                }

                byPid[pid] = new AppInfo
                {
                    Name = window.App,
                    Pid = window.Pid,
                    ProcessInstance = window.ProcessInstance,
                    Presentation = AppPresentation.Foreground,
                };
                order.Add(pid);
                continue;
            }

            if (window.State.IsFocused && !byPid[pid].MatchesIdentifier(window.App))
            {
                byPid[pid] = byPid[pid] with { Presentation = AppPresentation.Foreground };
            }
        }

        var inventory = new Core.Locator.AppInventory();
        foreach (var pid in order)
        {
            inventory.Apps.Add(byPid[pid]);
        }

        return inventory;
    }
}
