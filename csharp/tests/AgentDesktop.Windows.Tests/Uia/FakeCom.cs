using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.Tests;

/// <summary>A COM-shaped object built in native memory: a pointer cell holding a vtable the
/// test fills, with the three IUnknown slots present so a release on slot 2 is a real call.
/// The UI Automation slot a test wants is written at its index, so the pattern binding reads
/// the same slot a live provider's pointer carries, and no test reaches a real app.</summary>
internal sealed unsafe class FakeCom : IDisposable
{
    /// <summary>The pointer a fake element's `GetCurrentPattern` returns, set before the call
    /// and written to the out slot by the fake, which mirrors the live shape.</summary>
    internal static IntPtr LivePattern;

    /// <summary>Counts releases, so a test can show a pattern pointer is released once.</summary>
    internal static int ReleaseCount;

    /// <summary>The two int arguments the last slot call received, for a scroll test.</summary>
    internal static int FirstArgument;

    internal static int SecondArgument;

    /// <summary>UIA_E_ELEMENTNOTENABLED, the code a disabled control refuses a write with.</summary>
    internal const int ElementNotEnabled = unchecked((int)0x8004_0200);

    /// <summary>UIA_E_ELEMENTNOTAVAILABLE, the code a killed provider answers with.</summary>
    internal const int ElementNotAvailable = unchecked((int)0x8004_0201);

    private readonly IntPtr _self;
    private readonly IntPtr _vtable;

    internal FakeCom(int slots)
    {
        unsafe
        {
            _self = Marshal.AllocHGlobal(IntPtr.Size);
            _vtable = Marshal.AllocHGlobal((slots + 3) * IntPtr.Size);
            Marshal.WriteIntPtr(_vtable, (IntPtr)((delegate* unmanaged<IntPtr, int>)&QueryInterface));
            Marshal.WriteIntPtr(_vtable + IntPtr.Size, (IntPtr)((delegate* unmanaged<IntPtr, int>)&AddRef));
            Marshal.WriteIntPtr(_vtable + 2 * IntPtr.Size, (IntPtr)((delegate* unmanaged<IntPtr, int>)&Release));
            Marshal.WriteIntPtr(_self, _vtable);
        }
    }

    internal IntPtr Pointer => _self;

    internal static IntPtr FailingMethod
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int>)&MethodFails);
            }
        }
    }

    internal static IntPtr SucceedingMethod
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int>)&MethodSucceeds);
            }
        }
    }

    internal static IntPtr ProviderDiedMethod
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int>)&MethodProviderDied);
            }
        }
    }

    internal static IntPtr VerticallyScrollableGetter
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int*, int>)&ReadVerticallyScrollable);
            }
        }
    }

    internal static IntPtr HorizontallyScrollableGetter
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int*, int>)&ReadHorizontallyScrollable);
            }
        }
    }

    internal static IntPtr VerticalPercentGetter
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, double*, int>)&ReadVerticalPercent);
            }
        }
    }

    internal static IntPtr HorizontalPercentGetter
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, double*, int>)&ReadHorizontalPercent);
            }
        }
    }

    internal static IntPtr ScrollMethodPointer
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int, int, int>)&ScrollMethod);
            }
        }
    }

    internal static IntPtr LivePatternGetter
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int, IntPtr*, int>)&GetCurrentPatternReturnsLive);
            }
        }
    }

    internal static IntPtr AbsentPatternGetter
    {
        get
        {
            unsafe
            {
                return (IntPtr)((delegate* unmanaged<IntPtr, int, IntPtr*, int>)&GetCurrentPatternAbsent);
            }
        }
    }

    internal void SetSlot(int slot, IntPtr function)
    {
        unsafe
        {
            Marshal.WriteIntPtr(_vtable + slot * IntPtr.Size, function);
        }
    }

    public void Dispose()
    {
        Marshal.FreeHGlobal(_vtable);
        Marshal.FreeHGlobal(_self);
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(IntPtr self) => 0;

    [UnmanagedCallersOnly]
    private static int AddRef(IntPtr self) => 0;

    [UnmanagedCallersOnly]
    private static int Release(IntPtr self)
    {
        ReleaseCount++;
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int MethodFails(IntPtr self) => ElementNotEnabled;

    [UnmanagedCallersOnly]
    private static int MethodSucceeds(IntPtr self) => 0;

    [UnmanagedCallersOnly]
    private static int MethodProviderDied(IntPtr self) => ElementNotAvailable;

    [UnmanagedCallersOnly]
    private static int ReadVerticallyScrollable(IntPtr self, int* value)
    {
        *value = 1;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int ReadHorizontallyScrollable(IntPtr self, int* value)
    {
        *value = 0;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int ReadVerticalPercent(IntPtr self, double* value)
    {
        *value = 33.5;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int ReadHorizontalPercent(IntPtr self, double* value)
    {
        *value = 44.5;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int ScrollMethod(IntPtr self, int horizontal, int vertical)
    {
        FirstArgument = horizontal;
        SecondArgument = vertical;
        return 0;
    }

    /// <summary>`GetCurrentPattern` on slot 16: the pointer set in `LivePattern`, and success,
    /// the shape a live element returns for a pattern it exposes.</summary>
    [UnmanagedCallersOnly]
    private static int GetCurrentPatternReturnsLive(IntPtr self, int patternId, IntPtr* found)
    {
        *found = LivePattern;
        return 0;
    }

    /// <summary>`GetCurrentPattern` as UIA reports a pattern the element does not expose:
    /// S_FALSE with a null pointer.</summary>
    [UnmanagedCallersOnly]
    private static int GetCurrentPatternAbsent(IntPtr self, int patternId, IntPtr* found)
    {
        *found = IntPtr.Zero;
        return 1;
    }
}
