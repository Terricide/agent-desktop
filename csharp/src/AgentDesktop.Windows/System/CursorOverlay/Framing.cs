using System.Text.Json;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The control wire format and the single acknowledgement byte.
/// </summary>
/// <remarks>
/// Pure, and deliberately the same shape macOS uses: one JSON-encoded <c>CursorOverlayControl</c> per
/// connection under a size cap, answered by one byte. The cap matters because the payload arrives from
/// another process, and a framing that grows with its input is a framing an unbounded write can stall.
/// <para>
/// Which controls are acknowledged is part of the contract rather than an implementation detail: the caller of
/// a travel, a hide or a disable blocks on the answer, and so does the caller of the enable that decides
/// <c>data.rendered</c>. An effect control is fire-and-forget after dispatch has already confirmed, so waiting
/// on it would add latency to nothing.
/// </para>
/// </remarks>
internal static class Framing
{
    /// <summary>Matches the macOS transport's cap. A control is coordinates, a short label and a style; nothing legitimate approaches this.</summary>
    internal const int MaxControlBytes = 4096;

    /// <summary>The one byte the renderer answers with.</summary>
    internal const byte Acknowledgement = 1;

    /// <summary>Encodes one control, rejecting a payload past the control budget.</summary>
    internal static byte[] Encode(CursorOverlayControl control)
    {
        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(control, CursorOverlayJsonContext.Default.CursorOverlayControl);
        }
        catch (Exception error)
        {
            throw AdapterError.Internal("The cursor overlay control could not be encoded").WithPlatformDetail(error.Message);
        }

        if (bytes.Length > MaxControlBytes)
        {
            throw AdapterError.InvalidArgs("The cursor overlay control is larger than the transport accepts")
                .WithPlatformDetail($"{bytes.Length} bytes encoded, {MaxControlBytes} accepted");
        }

        return bytes;
    }

    /// <summary>Decodes one control, rejecting a payload past the control budget before parsing it.</summary>
    internal static CursorOverlayControl Decode(byte[] bytes)
    {
        if (bytes.Length > MaxControlBytes)
        {
            throw AdapterError.InvalidArgs("The cursor overlay control is larger than the transport accepts");
        }

        try
        {
            return JsonSerializer.Deserialize(bytes, CursorOverlayJsonContext.Default.CursorOverlayControl)
                ?? throw AdapterError.InvalidArgs("The cursor overlay control could not be decoded");
        }
        catch (Exception error)
        {
            throw AdapterError.InvalidArgs("The cursor overlay control could not be decoded").WithPlatformDetail(error.Message);
        }
    }

    /// <summary>
    /// Whether the sender of this control waits for the byte the renderer sends back.
    /// </summary>
    /// <remarks>
    /// An <c>Enable</c> is included and macOS's send path does not include it. That is a deliberate divergence:
    /// <c>data.rendered</c> is answered from this acknowledgement, and the bootstrap that starts the child is a
    /// one-way stdin write with no return path of its own.
    /// </remarks>
    internal static bool IsAcknowledged(CursorOverlayControl control) =>
        control.IsEnable || control.IsDisable || control.IsHide || control.IsTravel;

    /// <summary>
    /// Whether a control may bring a renderer into existence when none is running. A <c>Disable</c> that
    /// spawned one would start a renderer in order to tell it to stop, and <c>Hide</c>/<c>Show</c> are sent
    /// around every mutating command in a headed session, which would fork one per command.
    /// </summary>
    internal static bool MaySpawn(CursorOverlayControl control) =>
        control.IsEnable || control.Instruction is not null;
}
