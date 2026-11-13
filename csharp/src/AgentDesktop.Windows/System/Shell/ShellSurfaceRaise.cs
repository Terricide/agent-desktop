using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// Raises a shell surface through its declared mechanism. Keyboard chords are posted with <c>SendInput</c>
/// directly rather than through the shared keyboard seam, because that seam is stubbed in unit tests and the
/// live tests must reach the OS. The <c>SendInput</c> return value is never delivery evidence (A9-3): the chord
/// is posted and the open is verified by observation alone.
/// </summary>
internal static class ShellSurfaceRaise
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventfKeyUp = 0x0002;
    private const ushort KeyDownMask = 0x8000;
    private const long SettleMs = 10;

    /// <summary>
    /// Raises a surface through its declared mechanism: a surface already presented takes no input, an accelerator
    /// posts its chord, and the notification overflow is raised by invoking the shell's chevron control.
    /// </summary>
    internal static void RaiseRow(SurfaceKindRow row, Deadline deadline)
    {
        deadline.EnsureRemaining();
        switch (row.Raise)
        {
            case SurfaceRaise.AlreadyRaised:
                return;
            case SurfaceRaise.Accelerator accelerator:
                SendChord(accelerator.Modifiers, accelerator.Key, deadline);
                return;
            case SurfaceRaise.ChevronInvoke:
                throw InvokeOverflowChevron();
        }
    }

    /// <remarks>
    /// SEAM: ShellSurfaceRaise.cs:InvokeOverflowChevron needs the shared UI Automation client and the invoke
    /// pattern, neither ported to AgentDesktop.Windows.Uia. Until they are, the chevron raise refuses by name.
    /// </remarks>
    private static AdapterError InvokeOverflowChevron() =>
        AdapterError.NotSupported("raise the notification overflow");

    internal static void RaiseAccelerator(
        global::System.Collections.Generic.IReadOnlyList<ushort> modifiers,
        ushort key,
        Deadline deadline)
    {
        deadline.EnsureRemaining();
        SendChord(modifiers, key, deadline);
    }

    /// <summary>
    /// Presses each modifier, then the key, waits a settle interval capped by the remaining budget, releases the
    /// key, and releases the modifiers in reverse order. Any key still believed held when the chord unwinds early,
    /// including through a deadline error, is released by a sweep that runs only when the OS reports it down, so a
    /// key already released is never re-pressed.
    /// </summary>
    internal static void SendChord(
        global::System.Collections.Generic.IReadOnlyList<ushort> modifiers,
        ushort key,
        Deadline deadline)
    {
        global::System.Collections.Generic.List<ushort> held = new();
        try
        {
            foreach (ushort vk in modifiers)
            {
                deadline.EnsureRemaining();
                PostShellKey(vk, up: false);
                held.Add(vk);
            }
            deadline.EnsureRemaining();
            PostShellKey(key, up: false);
            held.Add(key);
            global::System.Threading.Thread.Sleep(SettleDuration(deadline));
            PostShellKey(key, up: true);
            held.RemoveAll(candidate => candidate == key);
            for (int index = modifiers.Count - 1; index >= 0; index--)
            {
                ushort vk = modifiers[index];
                deadline.EnsureRemaining();
                PostShellKey(vk, up: true);
                held.RemoveAll(candidate => candidate == vk);
            }
        }
        finally
        {
            for (int index = held.Count - 1; index >= 0; index--)
            {
                ushort vk = held[index];
                if (KeyCurrentlyDown(vk))
                {
                    PostShellKey(vk, up: true);
                }
            }
        }
    }

    private static int SettleDuration(Deadline deadline)
    {
        long remaining = deadline.RemainingMs();
        return (int)(remaining < SettleMs ? remaining : SettleMs);
    }

    private static void PostShellKey(ushort vk, bool up)
    {
        NativeKeyboardInput input = new()
        {
            Type = InputKeyboard,
            WVk = vk,
            WScan = 0,
            DwFlags = up ? KeyEventfKeyUp : 0u,
            Time = 0,
            DwExtraInfo = global::System.IntPtr.Zero,
        };
        _ = SendInput(1, new[] { input }, Marshal.SizeOf<NativeKeyboardInput>());
    }

    private static bool KeyCurrentlyDown(ushort vk) =>
        (unchecked((ushort)GetAsyncKeyState(vk)) & KeyDownMask) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeKeyboardInput[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>
    /// The keyboard arm of the Win32 <c>INPUT</c> structure on 64-bit Windows: the union begins at offset 8 and
    /// the structure is 40 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeKeyboardInput
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public ushort WVk;

        [FieldOffset(10)]
        public ushort WScan;

        [FieldOffset(12)]
        public uint DwFlags;

        [FieldOffset(16)]
        public uint Time;

        [FieldOffset(24)]
        public global::System.IntPtr DwExtraInfo;
    }
}
