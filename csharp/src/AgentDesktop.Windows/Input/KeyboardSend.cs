using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.Input;

/// <summary>One keyboard event to post: a virtual key, a scan code or UTF-16 unit, and the
/// flags saying whether this is a press, a release, or a direct Unicode unit.</summary>
internal readonly record struct KeyboardInputEvent(ushort Vk, ushort Scan, uint Flags);

/// <summary>The one seam that calls `SendInput` and `GetAsyncKeyState` for keyboard events.
/// `SendInput`'s return value is never treated as delivery evidence, so posting is
/// fire-and-forget and the outcome is judged by observation. The seam is two delegates so a
/// unit test records the events it would have posted and answers the live key-state read,
/// which the release guard's sweep depends on, without emitting real input.</summary>
internal static class KeyboardSend
{
    internal static Action<IReadOnlyList<KeyboardInputEvent>> PostSink = PostNative;

    internal static Func<ushort, bool> KeyStateSink = KeyStateNative;

    internal static void PostKeyboardInputs(IReadOnlyList<KeyboardInputEvent> events)
    {
        if (events.Count > 0)
        {
            PostSink(events);
        }
    }

    internal static bool KeyIsDown(ushort vk) => KeyStateSink(vk);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;

        public ushort wScan;

        public uint dwFlags;

        public uint time;

        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Input
    {
        [FieldOffset(0)] public uint type;

        [FieldOffset(8)] public KeybdInput ki;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static void PostNative(IReadOnlyList<KeyboardInputEvent> events)
    {
        var raw = new Input[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            raw[i] = new Input
            {
                type = Uia.KeyboardIds.InputKeyboard,
                ki = new KeybdInput
                {
                    wVk = events[i].Vk,
                    wScan = events[i].Scan,
                    dwFlags = events[i].Flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            };
        }

        SendInput((uint)raw.Length, raw, Marshal.SizeOf<Input>());
    }

    private static bool KeyStateNative(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
