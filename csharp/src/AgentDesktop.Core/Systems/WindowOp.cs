namespace AgentDesktop.Core.Systems;

/// <summary>A window operation a command asks the platform to perform. Each case is a nested record, so a pattern such as <c>WindowOp.Resize</c> matches one case.</summary>
public abstract record WindowOp
{
    /// <summary>Set the window's width and height.</summary>
    public sealed record Resize(double Width, double Height) : WindowOp;

    /// <summary>Set the window's top-left position.</summary>
    public sealed record Move(double X, double Y) : WindowOp;

    /// <summary>Minimize the window.</summary>
    public sealed record Minimize : WindowOp;

    /// <summary>Maximize the window.</summary>
    public sealed record Maximize : WindowOp;

    /// <summary>Restore the window from minimized or maximized.</summary>
    public sealed record Restore : WindowOp;
}
