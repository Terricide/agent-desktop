namespace AgentDesktop.Windows.Uia;

/// <summary>The UI Automation client, instantiated through CoCreateInstance with no COM
/// interop. It never initializes COM: the calling thread's apartment belongs to the process,
/// and the .NET runtime has already joined a managed thread to the multithreaded apartment
/// (measured: an apartment-threaded CoInitializeEx on it returns RPC_E_CHANGED_MODE). A
/// thread with no apartment fails creation with CO_E_NOTINITIALIZED. Every method returns
/// the call's HRESULT, and every pointer it hands out carries a reference the caller
/// releases exactly once.</summary>
public sealed class Client : IDisposable
{
    /// <summary>How long a call may wait to reach a target's provider. It is UI Automation's
    /// documented default, set explicitly so the bound is a decision, not an inheritance.</summary>
    public const int ConnectionTimeoutMs = 2_000;

    /// <summary>How long one UI Automation transaction may take end to end.</summary>
    public const int TransactionTimeoutMs = 20_000;

    private static readonly Guid CuiAutomation8 = new("e22ad333-b25f-460c-83d0-0581107395c9");

    private static readonly Guid IidIuiAutomation2 = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a");

    private IntPtr _pointer;

    /// <summary>Takes ownership of one reference to an IUIAutomation2 object.</summary>
    internal Client(IntPtr pointer) => _pointer = pointer;

    /// <summary>Creates a client whose calls are bounded, or returns the HRESULT that
    /// prevented it. Only CUIAutomation8 exposes IUIAutomation2 and its timeouts. The
    /// CUIAutomation object does not, so its calls carry no bound: the Rust adapter
    /// measured ElementFromHandle against a window that stopped dispatching run past a
    /// 30 s watchdog through it. There is deliberately no fallback to that client.</summary>
    public static int Create(out Client? client)
    {
        client = null;
        var hr = Calls.CoCreateInstance(in CuiAutomation8, IntPtr.Zero, 1, in IidIuiAutomation2, out var pointer);
        if (hr >= 0 && pointer != IntPtr.Zero)
        {
            hr = Calls.CallWithInt(pointer, Slots.Client2PutConnectionTimeout, ConnectionTimeoutMs);
        }

        if (hr >= 0 && pointer != IntPtr.Zero)
        {
            hr = Calls.CallWithInt(pointer, Slots.Client2PutTransactionTimeout, TransactionTimeoutMs);
        }

        if (hr < 0 || pointer == IntPtr.Zero)
        {
            Calls.Release(pointer);
            return hr;
        }

        client = new Client(pointer);
        return hr;
    }

    /// <summary>Compares two UI Automation elements by provider identity.</summary>
    public int CompareElements(IntPtr first, IntPtr second, out bool areSame)
    {
        var hr = Calls.CallTwoPointersIntOut(_pointer, Slots.ClientCompareElements, first, second, out var flag);
        areSame = hr >= 0 && flag != 0;
        return hr;
    }

    /// <summary>The element for a top-level window handle.</summary>
    public int ElementFromHandle(IntPtr hwnd, out IntPtr element) =>
        Calls.CallOutPointerWithArg(_pointer, Slots.ClientElementFromHandle, hwnd, out element);

    /// <summary>A point inside a window activates accessibility for a Chromium process, which
    /// a top-level handle read does not.</summary>
    public int ElementFromPoint(int x, int y, out IntPtr element) =>
        Calls.CallPointOut(_pointer, Slots.ClientElementFromPoint, new UiaPoint { x = x, y = y }, out element);

    /// <summary>The element that currently holds keyboard focus.</summary>
    public int FocusedElement(out IntPtr element) =>
        Calls.CallOutPointer(_pointer, Slots.ClientGetFocusedElement, out element);

    /// <summary>The raw view walker, which visits every element including the ones the
    /// control view hides.</summary>
    public int RawViewWalker(out IntPtr walker) =>
        Calls.CallOutPointer(_pointer, Slots.ClientRawViewWalker, out walker);

    /// <summary>A condition that matches every element, for a whole-subtree FindAll.</summary>
    public int TrueCondition(out IntPtr condition) =>
        Calls.CallOutPointer(_pointer, Slots.ClientCreateTrueCondition, out condition);

    /// <summary>An empty cache request: element scope, control-view filter, full element mode.</summary>
    public int CreateCacheRequest(out IntPtr request) =>
        Calls.CallOutPointer(_pointer, Slots.ClientCreateCacheRequest, out request);

    /// <summary>The connection bound this client carries, read back from the client.</summary>
    public int ConnectionTimeout(out int milliseconds) =>
        Calls.CallIntOut(_pointer, Slots.Client2GetConnectionTimeout, out milliseconds);

    /// <summary>The transaction bound this client carries, read back from the client.</summary>
    public int TransactionTimeout(out int milliseconds) =>
        Calls.CallIntOut(_pointer, Slots.Client2GetTransactionTimeout, out milliseconds);

    public void Dispose()
    {
        if (_pointer != IntPtr.Zero)
        {
            Calls.Release(_pointer);
            _pointer = IntPtr.Zero;
        }
    }
}
