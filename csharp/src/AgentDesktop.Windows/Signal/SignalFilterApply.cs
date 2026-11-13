using AgentDesktop.Core.Signal;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows.Signal;

/// <summary>Applies a <see cref="SignalFilter"/> to an assembled inventory. The filter intersects: a process constraint requires the exact pid and instance, and an app constraint additionally requires a case-insensitive exact image name. Completeness passes through unchanged.</summary>
internal static class SignalFilterApply
{
    internal static SignalWindowInventory Apply(SignalWindowInventory inventory, SignalFilter filter) =>
        new(
            inventory.Windows.Where(w => Matches(filter, w.Pid, w.ProcessInstance, w.App)).ToList(),
            inventory.Apps.Where(a => Matches(filter, a.Pid, a.ProcessInstance, a.Name)).ToList(),
            inventory.WindowsComplete,
            inventory.AppsComplete);

    internal static bool Matches(SignalFilter filter, ProcessId pid, string? processInstance, string appName)
    {
        var processMatches = filter.Process is null
            || (pid == filter.Process.Pid && processInstance == filter.Process.Instance);
        var appMatches = filter.App is null
            || string.Equals(appName, filter.App, StringComparison.OrdinalIgnoreCase);
        return processMatches && appMatches;
    }
}
