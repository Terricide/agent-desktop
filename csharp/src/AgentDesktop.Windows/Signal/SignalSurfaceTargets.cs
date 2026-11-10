using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows.Signal;

/// <summary>The identity a surface needs to be reported: the parts of an already filtered app that a surface signal carries.</summary>
internal sealed record SignalSurfaceTarget(ProcessId Pid, string Name, string Instance);

/// <summary>Selects the apps that can receive a surface signal. An app with no process instance is excluded rather than given a fabricated one.</summary>
internal static class SignalSurfaceTargets
{
    internal static Dictionary<ProcessId, SignalSurfaceTarget> Identified(List<AppInfo> apps)
    {
        var targets = new Dictionary<ProcessId, SignalSurfaceTarget>();
        foreach (var app in apps)
        {
            if (app.ProcessInstance is null)
            {
                continue;
            }

            targets[app.Pid] = new SignalSurfaceTarget(app.Pid, app.Name, app.ProcessInstance);
        }

        return targets;
    }
}
