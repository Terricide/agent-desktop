using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.Uia;

/// <summary>An OLE VARIANT: the type tag at offset 0 and the value union at offset 8. 24
/// bytes is the x64 size; where a VARIANT is smaller, the larger buffer is harmless.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct Variant
{
    public const ushort VtEmpty = 0;

    public const ushort VtNull = 1;

    public const ushort VtI4 = 3;

    public const ushort VtBstr = 8;

    public const ushort VtBool = 11;

    public const ushort VtUnknown = 13;

    public const ushort VtInt = 22;

    public const ushort VtVoid = 24;

    [FieldOffset(0)]
    public ushort Type;

    [FieldOffset(8)]
    public IntPtr Pointer;

    [FieldOffset(8)]
    public int Number;

    [FieldOffset(8)]
    public short Flag;
}
