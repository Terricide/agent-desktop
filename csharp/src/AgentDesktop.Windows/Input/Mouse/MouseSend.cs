using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.Input.Mouse;

/// <summary>One mouse event to post: a displacement or absolute point, wheel data, and the
/// `MOUSEEVENTF_*` flags that say what the event is.</summary>
internal readonly record struct MouseInputEvent(int Dx, int Dy, int MouseData, uint Flags);

/// <summary>The one seam that calls `SendInput` for mouse events.</summary>
/// <remarks>
/// `SendInput`'s return value is never treated as delivery evidence (probe A9-3: it reports
/// success in both the delivered and UIPI-blocked arms), so posting is best-effort and the
/// outcome is judged upstream where a re-read exists. The seam is a delegate so a unit test
/// records the events it would have posted without emitting real input.
/// </remarks>
internal static class MouseSend
{
    internal const uint MouseEventfMove = 0x0001;
    internal const uint MouseEventfLeftDown = 0x0002;
    internal const uint MouseEventfLeftUp = 0x0004;
    internal const uint MouseEventfRightDown = 0x0008;
    internal const uint MouseEventfRightUp = 0x0010;
    internal const uint MouseEventfMiddleDown = 0x0020;
    internal const uint MouseEventfMiddleUp = 0x0040;
    internal const uint MouseEventfWheel = 0x0800;
    internal const uint MouseEventfHWheel = 0x1000;
    internal const uint MouseEventfVirtualDesk = 0x4000;
    internal const uint MouseEventfAbsolute = 0x8000;

    private const uint InputMouse = 0;

    internal static Action<IReadOnlyList<MouseInputEvent>> PostSink = PostNative;

    internal static void PostMouseInputs(IReadOnlyList<MouseInputEvent> events)
    {
        if (events.Count > 0)
        {
            PostSink(events);
        }
    }

    /// <summary>Absolute-move input for an already-normalized point.</summary>
    /// <remarks>
    /// Lives beside the button constructor because the move and button batches are built by
    /// the synthesis path, the drag guard and the click guard alike; separate copies of these
    /// constructors would let the flag set drift between the normal post and the corrective
    /// one an abort depends on. The point is passed as its three components.
    /// </remarks>
    internal static MouseInputEvent MoveInput(int x, int y, bool virtualDesktop)
    {
        var flags = MouseEventfMove | MouseEventfAbsolute;
        if (virtualDesktop)
        {
            flags |= MouseEventfVirtualDesk;
        }

        return new MouseInputEvent(x, y, 0, flags);
    }

    /// <summary>Button-state input carrying only a `MOUSEEVENTF_*` down/up flag.</summary>
    internal static MouseInputEvent ButtonInput(uint flag) => new(0, 0, 0, flag);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseRawInput
    {
        public uint Type;

        public MouseRawData Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseRawData
    {
        public int Dx;

        public int Dy;

        public uint MouseData;

        public uint DwFlags;

        public uint Time;

        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern uint SendInput(uint nInputs, MouseRawInput[] pInputs, int cbSize);

    private static void PostNative(IReadOnlyList<MouseInputEvent> events)
    {
        var raw = new MouseRawInput[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            raw[i] = ToRaw(events[i]);
        }

        SendInput((uint)raw.Length, raw, Marshal.SizeOf<MouseRawInput>());
    }

    private static MouseRawInput ToRaw(MouseInputEvent input) => new()
    {
        Type = InputMouse,
        Mi = new MouseRawData
        {
            Dx = input.Dx,
            Dy = input.Dy,
            MouseData = unchecked((uint)input.MouseData),
            DwFlags = input.Flags,
            Time = 0,
            DwExtraInfo = IntPtr.Zero,
        },
    };
}
