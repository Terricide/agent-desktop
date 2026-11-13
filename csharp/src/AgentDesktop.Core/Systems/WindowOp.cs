namespace AgentDesktop.Core.Systems;

/// <summary>The window change one window command asks for. The geometry cases carry the rectangle the
/// command requested; the state cases carry none, because a minimize, maximize, or restore names a
/// placement the window must reach, not a size. Port of <c>WindowOp</c> in
/// <c>crates/core/src/system/window_op.rs</c>.</summary>
public abstract record WindowOp
{
    public sealed record Resize(double Width, double Height) : WindowOp;

    public sealed record Move(double X, double Y) : WindowOp;

    public sealed record Minimize : WindowOp;

    public sealed record Maximize : WindowOp;

    public sealed record Restore : WindowOp;

    /// <summary>The command name the report carries, which is the CLI name, not the operation word.
    /// The state verbs report the verb alone; the geometry writes report the full command name.</summary>
    public string CommandName() => this switch
    {
        Resize => "resize-window",
        Move => "move-window",
        Minimize => "minimize",
        Maximize => "maximize",
        Restore => "restore",
        _ => "window",
    };

    /// <summary>The operation word the tie refusal names, so a resize that found two candidates says
    /// what it was trying to resize. This is the verb alone, not the command name.</summary>
    public string Verb() => this switch
    {
        Resize => "resize",
        Move => "move",
        Minimize => "minimize",
        Maximize => "maximize",
        Restore => "restore",
        _ => "window",
    };

    /// <summary>The step label naming the platform call that carried the operation, so the report says
    /// which mechanism the window was written through.</summary>
    public string StepLabel() => this switch
    {
        Resize => "window_resize",
        Move => "window_move",
        Minimize => "window_minimize",
        Maximize => "window_maximize",
        Restore => "window_restore",
        _ => "window_op",
    };
}
