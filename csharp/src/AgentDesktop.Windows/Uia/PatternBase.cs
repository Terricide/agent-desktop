using System;
using AgentDesktop.Windows.Actions;

namespace AgentDesktop.Windows.Uia;

/// <summary>A pattern interface pointer an element returned, owned until disposal releases
/// it exactly once. Each method calls the vtable slot the UI Automation specification fixes
/// for it, the way `uiautomation-rs` calls a pattern trait method. A negative return is the
/// callee's HRESULT and becomes a `UiaException` naming the method that failed, which is
/// what `classify_write` receives in Rust. A pattern never called through a zero pointer.</summary>
internal abstract class PatternBase : IDisposable
{
    private IntPtr _pointer;

    protected PatternBase(IntPtr pointer, string mechanism)
    {
        _pointer = pointer;
        Mechanism = mechanism;
    }

    protected string Mechanism { get; }

    protected void Invoke(int methodSlot, string method)
    {
        ThrowOnFailure(Calls.CallVoid(_pointer, methodSlot), method);
    }

    protected void InvokeWithInts(int methodSlot, string method, int first, int second)
    {
        ThrowOnFailure(Calls.CallWithTwoInts(_pointer, methodSlot, first, second), method);
    }

    protected bool ReadFlag(int methodSlot, string method)
    {
        var hr = Calls.CallIntOut(_pointer, methodSlot, out var value);
        ThrowOnFailure(hr, method);
        return value != 0;
    }

    protected double ReadPercent(int methodSlot, string method)
    {
        var hr = Calls.CallDoubleOut(_pointer, methodSlot, out var value);
        ThrowOnFailure(hr, method);
        return value;
    }

    public void Dispose()
    {
        var pointer = _pointer;
        _pointer = IntPtr.Zero;
        Calls.Release(pointer);
        GC.SuppressFinalize(this);
    }

    private void ThrowOnFailure(int hr, string method)
    {
        if (hr < 0)
        {
            throw new UiaException(WriteFailure.FromHresult(hr), method, Mechanism);
        }
    }
}
