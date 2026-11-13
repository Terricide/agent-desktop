using System.Text.Json.Serialization;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Session;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// One message to a session's cursor overlay renderer: turn the overlay on,
/// show one presentation, hide, show, or turn the overlay off. The wire is flat
/// and carries the action name, the same shape the Rust renderer reads.
/// </summary>
public sealed record CursorOverlayControl
{
    /// <summary>The greeting an overlay opens with when no label is asked for.</summary>
    public const string Greeting = "Hey, let's play with this computer!";

    /// <summary>What the renderer is asked to do.</summary>
    [property: JsonPropertyName("action")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<CursorOverlayControlAction>))]
    public required CursorOverlayControlAction Action { get; set; }

    /// <summary>The session whose renderer reads this control.</summary>
    [property: JsonPropertyName("session_id")]
    public required string SessionId { get; set; }

    /// <summary>The greeting an enable carries. A presentation's label lives in its instruction.</summary>
    [property: JsonPropertyName("label")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EnableLabel { get; set; }

    /// <summary>The agent this control is addressed to, when the session splits overlays per agent.</summary>
    [property: JsonPropertyName("agent_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentId { get; set; }

    /// <summary>The presentation to show, for a present control.</summary>
    [property: JsonPropertyName("instruction")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CursorOverlayInstruction? Instruction { get; set; }

    /// <summary>How the cursor looks, for an enable or a present control.</summary>
    [property: JsonPropertyName("style")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CursorOverlayStyle? Style { get; set; }

    /// <summary>How the cursor travels, for an enable or a present control.</summary>
    [property: JsonPropertyName("motion")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CursorMotionProfile? Motion { get; set; }

    /// <summary>
    /// The label the renderer shows: the greeting for an enable, the
    /// presentation's label for a present control, and nothing for the rest.
    /// </summary>
    public string? Label => Action switch
    {
        CursorOverlayControlAction.Enable => EnableLabel,
        CursorOverlayControlAction.Present => Instruction?.Label,
        _ => null,
    };

    /// <summary>Whether this control turns the overlay on.</summary>
    public bool IsEnable => Action == CursorOverlayControlAction.Enable;

    /// <summary>Whether this control shows one presentation.</summary>
    public bool IsPresent => Action == CursorOverlayControlAction.Present;

    /// <summary>Whether this control hides the overlay.</summary>
    public bool IsHide => Action == CursorOverlayControlAction.Hide;

    /// <summary>Whether this control shows the overlay again.</summary>
    public bool IsShow => Action == CursorOverlayControlAction.Show;

    /// <summary>Whether this control turns the overlay off.</summary>
    public bool IsDisable => Action == CursorOverlayControlAction.Disable;

    /// <summary>Whether this control is a hide or a show, which the renderer answers without waking.</summary>
    public bool IsTransient => IsHide || IsShow;

    /// <summary>
    /// Whether this control asks the cursor to travel, so its action must wait
    /// for the renderer to say the cursor arrived.
    /// </summary>
    public bool IsTravel => Instruction is { } instruction
        && (instruction.Phase == CursorPhase.Travel || instruction.Phase == CursorPhase.Drag);

    /// <summary>Whether this control is a caret follow after a key press.</summary>
    public bool IsCaretFollow => Instruction is { } instruction
        && instruction.Phase == CursorPhase.CaretFollow;

    /// <summary>
    /// Whether this control must be acknowledged before its action dispatches.
    /// </summary>
    public bool ExpectsAcknowledgement =>
        IsHide || IsShow || IsDisable || IsTravel
        || Instruction is { } instruction
        && (instruction.Phase == CursorPhase.Effect
            || instruction.Phase == CursorPhase.Caret
            || instruction.Phase == CursorPhase.CaretFollow);

    /// <summary>The control that turns <paramref name="session_id"/>'s overlay on, with the shipped greeting.</summary>
    public static CursorOverlayControl Enable(string session_id, CursorOverlayStyle style) => new()
    {
        Action = CursorOverlayControlAction.Enable,
        SessionId = session_id,
        EnableLabel = Greeting,
        Style = style,
        Motion = new CursorMotionProfile(),
    };

    /// <summary>The control that shows one presentation with the shipped style.</summary>
    public static CursorOverlayControl Present(string session_id, CursorOverlayInstruction instruction) =>
        PresentWithStyle(session_id, instruction, new CursorOverlayStyle());

    /// <summary>The control that shows one presentation with <paramref name="style"/>.</summary>
    public static CursorOverlayControl PresentWithStyle(
        string session_id,
        CursorOverlayInstruction instruction,
        CursorOverlayStyle style) => new()
    {
        Action = CursorOverlayControlAction.Present,
        SessionId = session_id,
        Instruction = instruction,
        Style = style,
        Motion = new CursorMotionProfile(),
    };

    /// <summary>The control that turns <paramref name="session_id"/>'s overlay off.</summary>
    public static CursorOverlayControl Disable(string session_id) => new()
    {
        Action = CursorOverlayControlAction.Disable,
        SessionId = session_id,
    };

    /// <summary>The control that hides <paramref name="session_id"/>'s overlay.</summary>
    public static CursorOverlayControl Hide(string session_id) => new()
    {
        Action = CursorOverlayControlAction.Hide,
        SessionId = session_id,
    };

    /// <summary>The control that shows <paramref name="session_id"/>'s overlay again.</summary>
    public static CursorOverlayControl Show(string session_id) => new()
    {
        Action = CursorOverlayControlAction.Show,
        SessionId = session_id,
    };

    /// <summary>
    /// The control addressed to <paramref name="agent_id"/>. A disable carries
    /// no agent, so it keeps the control as it is.
    /// </summary>
    public CursorOverlayControl WithAgentId(string? agent_id) =>
        IsDisable ? this : this with { AgentId = agent_id };

    /// <summary>
    /// The control that travels by <paramref name="motion"/>. Only an enable and
    /// a presentation carry a motion profile.
    /// </summary>
    public CursorOverlayControl WithMotion(CursorMotionProfile? motion) =>
        IsEnable || IsPresent ? this with { Motion = motion } : this;

    /// <summary>
    /// Rejects a session id the store cannot keep, an agent id it cannot keep, a
    /// presentation whose destination is out of range, a style that cannot be
    /// drawn, and a motion profile the renderer cannot travel by.
    /// </summary>
    public void Validate()
    {
        if (!SessionIdValidation.IsValid(SessionId))
            throw AdapterError.InvalidArgs("Invalid cursor overlay session id").Unsent();

        if (AgentId is not null && !AgentIdValidation.IsValid(AgentId))
            throw AdapterError.InvalidArgs("Invalid cursor overlay agent id").Unsent();

        if (IsPresent)
            Instruction?.Validate();

        if (Style is { } style)
            style.Validated();

        if (Motion is { } motion)
            motion.Validate();
    }
}
