using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows;

/// <summary>The focused application as the desktop reports it: the foreground window's owning
/// process, named by the image the kernel reported. A foreground window whose owner the kernel will
/// not name has no focused app, and the answer is nothing, not a name guessed from the title.</summary>
internal static class FocusedAppReader
{
    internal static AppInfo? Read()
    {
        var foreground = Interop.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return null;
        }

        var pid = WindowEnum.LiveWindowOwner(foreground);
        if (pid is null)
        {
            return null;
        }

        var name = ProcessSnapshot.Capture().ImageNameFor(pid.Value);
        if (name is null)
        {
            return null;
        }

        return new AppInfo
        {
            Name = name,
            Pid = ProcessId.From(pid.Value),
            ProcessInstance = ProcessTimes.TokenForPid(pid.Value),
            Presentation = AppPresentation.Foreground,
        };
    }
}
