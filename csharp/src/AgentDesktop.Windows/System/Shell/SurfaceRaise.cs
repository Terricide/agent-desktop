namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// The three raise mechanisms measurement found: two surfaces are already up, two come up on a shell accelerator, and
/// the overflow is raised by invoking a control rather than by any key the shell listens for.
/// </summary>
internal abstract record SurfaceRaise
{
    /// <summary>The surface is already presented; raising it takes no input.</summary>
    internal sealed record AlreadyRaised : SurfaceRaise;

    /// <summary>A shell accelerator chord: the modifier virtual-key codes held while <see cref="Key"/> is pressed.</summary>
    internal sealed record Accelerator(IReadOnlyList<ushort> Modifiers, ushort Key) : SurfaceRaise;

    /// <summary>The surface is raised by invoking the shell's overflow chevron control.</summary>
    internal sealed record ChevronInvoke : SurfaceRaise;
}
