using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>One row of the ToolHelp thread table, in the field order the platform declares (<c>THREADENTRY32</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ThreadEntry
{
    internal uint Size;
    internal uint Usage;
    internal uint ThreadId;
    internal uint OwnerProcessId;
    internal int BasePriority;
    internal int DeltaPriority;
    internal uint Flags;
}
