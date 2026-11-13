using System.Runtime.InteropServices;

namespace UiaAotProbe;

/// UIA3 COM access without the managed COM marshalling layer. Native AOT refuses
/// `Marshal.GetObjectForIUnknown` for a ComImport interface ("COM Interop requires a
/// ComWrapper instance registered for marshalling"), so the interface pointer is read
/// through its vtable with `delegate* unmanaged`, the same shape the Rust uia crate uses.
/// Every UIA accessor returns an HRESULT and writes its result through an out pointer,
/// so the out form is the one that is correct; reading the return register as a pointer
/// is an access violation. Slot numbers are the IUnknown order in UIAutomationClient.h,
/// where slots 0-2 are QueryInterface, AddRef and Release.
internal static class Uia
{
    public static readonly Guid CuiAutomation8 = new("e22ad333-b25f-460c-83d0-0581107395c9");

    public static readonly Guid CuiAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    public static readonly Guid IidIUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    public const int PatternInvoke = 10000;

    public const int PatternToggle = 10018;

    public const int PatternValue = 10002;

    public const int PatternExpandCollapse = 10010;

    public const int PatternSelectionItem = 10013;

    public const int PatternScroll = 10009;

    public const int PatternScrollItem = 10015;

    public const int IuiAutomationElementFromHandle = 6;

    public const int IuiAutomationElementFromPoint = 7;

    public const int IuiAutomationGetRawViewWalker = 16;

    public const int WalkerGetFirstChild = 4;

    public const int WalkerGetNextSibling = 6;

    public const int ElementGetName = 23;

    public const int ElementGetAutomationId = 29;

    public const int ElementGetControlType = 21;

    public const int ElementGetFrameworkId = 40;

    public const int ElementGetClassName = 30;

    public const int ElementGetIsEnabled = 28;

    public const int ElementGetIsOffscreen = 38;

    public const int ElementGetHasKeyboardFocus = 26;

    public const int ElementGetIsKeyboardFocusable = 27;

    public const int ElementGetBoundingRectangle = 43;

    public const int ElementFindAll = 6;

    public const int IuiAutomationCreateTrueCondition = 21;

    public const int ElementArrayGetLength = 3;

    public const int ElementArrayGetElement = 4;

    public const int ElementGetCurrentPattern = 16;

    public const int InvokePatternInvoke = 3;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(in Guid rclsid, IntPtr outer, int context, in Guid iid, out IntPtr pointer);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, int cooldown);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public static IntPtr Slot(IntPtr comPointer, int slot) =>
        Marshal.ReadIntPtr(Marshal.ReadIntPtr(comPointer), slot * IntPtr.Size);

    public static void Release(IntPtr comPointer)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int>)(Slot(comPointer, 2));
            fn(comPointer);
        }
    }

    public static IntPtr CallOutPointer(IntPtr comPointer, int slot)
    {
        var result = IntPtr.Zero;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            fn(comPointer, &result);
        }

        return result;
    }

    public static IntPtr CallOutPointerWithArg(IntPtr comPointer, int slot, IntPtr argument)
    {
        var result = IntPtr.Zero;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            fn(comPointer, argument, &result);
        }

        return result;
    }

    public static IntPtr CallOutPointerWithIntArg(IntPtr comPointer, int slot, int argument)
    {
        var result = IntPtr.Zero;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, IntPtr*, int>)(Slot(comPointer, slot));
            fn(comPointer, argument, &result);
        }

        return result;
    }

    public static IntPtr CallOutPointerWithIntAndPointer(IntPtr comPointer, int slot, int a, IntPtr b)
    {
        var result = IntPtr.Zero;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            fn(comPointer, a, b, &result);
        }

        return result;
    }

    public static int CallVoid(IntPtr comPointer, int slot)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int>)(Slot(comPointer, slot));
            return fn(comPointer);
        }
    }

    public static IntPtr CallPointOut(IntPtr comPointer, int slot, UiaPoint point)
    {
        var result = IntPtr.Zero;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, UiaPoint, IntPtr*, int>)(Slot(comPointer, slot));
            fn(comPointer, point, &result);
        }

        return result;
    }

    public static UiaRect CallRectOut(IntPtr comPointer, int slot)
    {
        var result = default(UiaRect);
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, UiaRect*, int>)(Slot(comPointer, slot));
            fn(comPointer, &result);
        }

        return result;
    }

    public static int CallIntOut(IntPtr comPointer, int slot)
    {
        var result = 0;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int*, int>)(Slot(comPointer, slot));
            fn(comPointer, &result);
        }

        return result;
    }

    public static bool CallBoolOut(IntPtr comPointer, int slot)
    {
        var result = (short)0;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, short*, int>)(Slot(comPointer, slot));
            fn(comPointer, &result);
        }

        return result != 0;
    }

    public static string CallStringOut(IntPtr comPointer, int slot)
    {
        var text = IntPtr.Zero;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            fn(comPointer, &text);
        }

        if (text == IntPtr.Zero)
        {
            return "";
        }

        var value = Marshal.PtrToStringBSTR(text);
        Marshal.FreeBSTR(text);
        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UiaPoint
    {
        public int x;

        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UiaRect
    {
        public double left;

        public double top;

        public double right;

        public double bottom;
    }
}
