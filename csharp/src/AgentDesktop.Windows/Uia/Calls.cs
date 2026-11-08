using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.Uia;

/// <summary>Reads one COM vtable slot and calls it through a function pointer. Native AOT
/// does not support COM interop, so every UI Automation call goes through these typed
/// pointers. Nothing checks a pointer's type against the method in its slot, so each helper's
/// shape is matched to the header by hand and confirmed by a live read. Every call returns the callee's HRESULT, so a failed read is never mistaken for an empty
/// one. Pointers returned by a UIA call are owned by the caller and released exactly once.</summary>
public static class Calls
{
    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(in Guid rclsid, IntPtr outer, int context, in Guid iid, out IntPtr pointer);

    /// <summary>Frees what a VARIANT owns: a BSTR is freed, an interface pointer released.</summary>
    [DllImport("oleaut32.dll")]
    public static extern int VariantClear(ref Variant variant);

    /// <summary>The singleton a property read carries as VT_UNKNOWN when the provider does
    /// not support the property.</summary>
    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaGetReservedNotSupportedValue(out IntPtr notSupported);

    public static IntPtr Slot(IntPtr comPointer, int slot) =>
        Marshal.ReadIntPtr(Marshal.ReadIntPtr(comPointer), slot * IntPtr.Size);

    public static void Release(IntPtr comPointer)
    {
        if (comPointer == IntPtr.Zero)
        {
            return;
        }

        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int>)(Slot(comPointer, 2));
            fn(comPointer);
        }
    }

    public static int CallOutPointer(IntPtr comPointer, int slot, out IntPtr result)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, &value);
        }

        result = value;
        return hr;
    }

    public static int CallOutPointerWithArg(IntPtr comPointer, int slot, IntPtr argument, out IntPtr result)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, argument, &value);
        }

        result = value;
        return hr;
    }

    public static int CallOutPointerWithIntArg(IntPtr comPointer, int slot, int argument, out IntPtr result)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, IntPtr*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, argument, &value);
        }

        result = value;
        return hr;
    }

    public static int CallOutPointerWithIntAndPointer(IntPtr comPointer, int slot, int first, IntPtr second, out IntPtr result)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, first, second, &value);
        }

        result = value;
        return hr;
    }

    /// <summary>IUIAutomationElement::FindAllBuildCache: the matches in a scope, each with the
    /// cache request's properties already fetched in the same cross-process call.</summary>
    public static int FindAllBuildCache(IntPtr element, int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)(Slot(element, Slots.ElementFindAllBuildCache));
            hr = fn(element, scope, condition, cacheRequest, &value);
        }

        found = value;
        return hr;
    }

    public static int CallVoid(IntPtr comPointer, int slot)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int>)(Slot(comPointer, slot));
            return fn(comPointer);
        }
    }

    public static int CallWithInt(IntPtr comPointer, int slot, int argument)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, int>)(Slot(comPointer, slot));
            return fn(comPointer, argument);
        }
    }

    public static int CallWithTwoInts(IntPtr comPointer, int slot, int first, int second)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, int, int>)(Slot(comPointer, slot));
            return fn(comPointer, first, second);
        }
    }

    public static int CallWithPointer(IntPtr comPointer, int slot, IntPtr argument)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr, int>)(Slot(comPointer, slot));
            return fn(comPointer, argument);
        }
    }

    /// <summary>Allocates a BSTR for the call and frees it before returning.</summary>
    public static int CallWithBstr(IntPtr comPointer, int slot, string text)
    {
        var bstr = Marshal.StringToBSTR(text);
        try
        {
            unsafe
            {
                var fn = (delegate* unmanaged<IntPtr, IntPtr, int>)(Slot(comPointer, slot));
                return fn(comPointer, bstr);
            }
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    public static int CallWithDouble(IntPtr comPointer, int slot, double argument)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, double, int>)(Slot(comPointer, slot));
            return fn(comPointer, argument);
        }
    }

    public static int CallWithTwoDoubles(IntPtr comPointer, int slot, double first, double second)
    {
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, double, double, int>)(Slot(comPointer, slot));
            return fn(comPointer, first, second);
        }
    }

    public static int CallTwoPointersIntOut(IntPtr comPointer, int slot, IntPtr first, IntPtr second, out int result)
    {
        var value = 0;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, first, second, &value);
        }

        result = value;
        return hr;
    }

    /// <summary>A BSTR getter. A null BSTR is COM's canonical empty string and reads as null;
    /// an allocated BSTR is copied and freed here.</summary>
    public static int CallStringOut(IntPtr comPointer, int slot, out string? text)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, IntPtr*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, &value);
        }

        text = value == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(value);
        if (value != IntPtr.Zero)
        {
            Marshal.FreeBSTR(value);
        }

        return hr;
    }

    /// <summary>An int or BOOL getter. A Win32 BOOL is four bytes, so it reads here too.</summary>
    public static int CallIntOut(IntPtr comPointer, int slot, out int result)
    {
        var value = 0;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, &value);
        }

        result = value;
        return hr;
    }

    public static int CallDoubleOut(IntPtr comPointer, int slot, out double result)
    {
        var value = 0.0;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, double*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, &value);
        }

        result = value;
        return hr;
    }

    public static int CallRectOut(IntPtr comPointer, int slot, out NativeRect result)
    {
        var value = default(NativeRect);
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, NativeRect*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, &value);
        }

        result = value;
        return hr;
    }

    public static int CallPointOut(IntPtr comPointer, int slot, UiaPoint point, out IntPtr result)
    {
        var value = IntPtr.Zero;
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, UiaPoint, IntPtr*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, point, &value);
        }

        result = value;
        return hr;
    }

    /// <summary>A property read by id into a VARIANT the caller must pass to VariantClear.</summary>
    public static int CallVariantOut(IntPtr comPointer, int slot, int propertyId, out Variant result)
    {
        var value = default(Variant);
        int hr;
        unsafe
        {
            var fn = (delegate* unmanaged<IntPtr, int, Variant*, int>)(Slot(comPointer, slot));
            hr = fn(comPointer, propertyId, &value);
        }

        result = value;
        return hr;
    }
}

/// <summary>A UIA screen point, passed by value as two ints in one register.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UiaPoint
{
    public int x;

    public int y;
}

/// <summary>The Win32 RECT the client API's BoundingRectangle getters write: four 32-bit
/// edges in device pixels. The provider-side UiaRect of doubles is a different layout, and
/// reading this getter through it returns garbage.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int left;

    public int top;

    public int right;

    public int bottom;
}
