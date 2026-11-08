namespace AgentDesktop.Windows.Uia;

/// <summary>Virtual-key codes and `SendInput` keyboard flags, as the Windows SDK
/// declares them. Every number is the value `winuser.h` names, never a count, so a
/// chord presses the key the name says.</summary>
public static class KeyboardIds
{
    public const ushort VkBack = 0x08;

    public const ushort VkTab = 0x09;

    public const ushort VkReturn = 0x0D;

    public const ushort VkShift = 0x10;

    public const ushort VkControl = 0x11;

    public const ushort VkMenu = 0x12;

    public const ushort VkEscape = 0x1B;

    public const ushort VkSpace = 0x20;

    public const ushort VkPrior = 0x21;

    public const ushort VkNext = 0x22;

    public const ushort VkEnd = 0x23;

    public const ushort VkHome = 0x24;

    public const ushort VkLeft = 0x25;

    public const ushort VkUp = 0x26;

    public const ushort VkRight = 0x27;

    public const ushort VkDown = 0x28;

    public const ushort VkDelete = 0x2E;

    public const ushort VkLWin = 0x5B;

    public const ushort VkF1 = 0x70;

    public const ushort VkF12 = 0x7B;

    /// <summary>Posts a key release. A down event carries no flag.</summary>
    public const uint KeyEventKeyUp = 0x0002;

    /// <summary>Posts a UTF-16 code unit directly, bypassing the layout's key table.</summary>
    public const uint KeyEventUnicode = 0x0004;

    /// <summary>The `SendInput` event type for a keyboard event.</summary>
    public const uint InputKeyboard = 1;

    /// <summary>Bit positions `VkKeyScanW` packs in its high byte: the modifiers the
    /// active layout needs to produce the requested character.</summary>
    public const byte LayoutShift = 0x01;

    public const byte LayoutControl = 0x02;

    public const byte LayoutAlt = 0x04;
}
