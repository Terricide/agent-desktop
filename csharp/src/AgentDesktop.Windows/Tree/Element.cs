using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>One IUIAutomationElement pointer, owned from construction until disposal
/// releases it, exactly once. Every cached read comes from the element's cache, which the
/// walk's cache request filled when the element was retrieved; live reads query the
/// provider directly for action preflight and post-action verification. A failed read, a
/// property the request did not name, and a zero pointer all read as unknown, never as an
/// empty answer. A zero pointer is never called through, so no read on it can fault.</summary>
internal sealed class Element : IDisposable
{
    /// <summary>Text longer than this is refused as unknown evidence rather than carried,
    /// the same bound the Rust adapter's property reads apply.</summary>
    internal const int MaxEvidenceChars = 2048;

    private static IntPtr _notSupported;

    private IntPtr _pointer;

    internal Element(IntPtr pointer) => _pointer = pointer;

    internal IntPtr Pointer => _pointer;

    internal LocatorField<string> Name() => Text(Slots.ElementGetCachedName);

    internal LocatorField<string> AutomationId() => Text(Slots.ElementGetCachedAutomationId);

    internal LocatorField<string> ClassName() => Text(Slots.ElementGetCachedClassName);

    internal LocatorField<string> FrameworkId() => Text(Slots.ElementGetCachedFrameworkId);

    internal LocatorField<int> ControlType() => Number(Slots.ElementGetCachedControlType);

    internal LocatorField<bool> IsEnabled() => Flag(Slots.ElementGetCachedIsEnabled);

    internal LocatorField<bool> IsOffscreen() => Flag(Slots.ElementGetCachedIsOffscreen);

    internal LocatorField<bool> HasKeyboardFocus() => Flag(Slots.ElementGetCachedHasKeyboardFocus);

    internal LocatorField<bool> IsKeyboardFocusable() => Flag(Slots.ElementGetCachedIsKeyboardFocusable);

    internal LocatorField<string> CurrentName() => Text(Slots.ElementGetName);

    internal LocatorField<string> CurrentAutomationId() => Text(Slots.ElementGetAutomationId);

    internal LocatorField<string> CurrentClassName() => Text(Slots.ElementGetClassName);

    internal LocatorField<int> CurrentControlType() => Number(Slots.ElementGetControlType);

    internal LocatorField<bool> CurrentIsEnabled() => Flag(Slots.ElementGetIsEnabled);

    internal LocatorField<bool> CurrentIsOffscreen() => Flag(Slots.ElementGetIsOffscreen);

    internal LocatorField<bool> CurrentHasKeyboardFocus() => Flag(Slots.ElementGetHasKeyboardFocus);

    internal LocatorField<bool> CurrentIsKeyboardFocusable() => Flag(Slots.ElementGetIsKeyboardFocusable);

    /// <summary>Screen bounds in device pixels from the walk cache. A negative extent reads as zero.</summary>
    internal LocatorField<Rect> BoundingRectangle() => ReadBounds(Slots.ElementGetCachedBoundingRectangle);

    /// <summary>Live screen bounds in device pixels.</summary>
    internal LocatorField<Rect> CurrentBoundingRectangle() => ReadBounds(Slots.ElementGetBoundingRectangle);

    /// <summary>Any cached property by id, classified the way the Rust adapter classifies a
    /// VARIANT: an empty, null or void VARIANT and the not-supported sentinel are absent; a
    /// BOOL, an integer and a string are known; any other shape is unknown.</summary>
    internal LocatorField<object> Property(int propertyId) => ReadProperty(Slots.ElementGetCachedPropertyValue, propertyId);

    /// <summary>Any live property by id, queried directly from the provider.</summary>
    internal LocatorField<object> CurrentProperty(int propertyId) => ReadProperty(Slots.ElementGetCurrentPropertyValue, propertyId);

    /// <summary>Queries the element for a pattern interface by id. The caller must release a non-zero pattern pointer.</summary>
    internal int CurrentPattern(int patternId, out IntPtr pattern)
    {
        if (_pointer == IntPtr.Zero)
        {
            pattern = IntPtr.Zero;
            return UiaTreeSource.InvalidPointer;
        }

        return Calls.CallOutPointerWithIntArg(_pointer, Slots.ElementGetCurrentPattern, patternId, out pattern);
    }

    /// <summary>Calls IUIAutomationElement::SetFocus on this element.</summary>
    internal int SetFocus() =>
        _pointer == IntPtr.Zero ? UiaTreeSource.InvalidPointer : Calls.CallVoid(_pointer, Slots.ElementSetFocus);

    public void Dispose()
    {
        var pointer = _pointer;
        _pointer = IntPtr.Zero;
        Calls.Release(pointer);
    }

    internal static bool ExceedsEvidenceBound(string text) =>
        text.Length > MaxEvidenceChars && text.EnumerateRunes().Count() > MaxEvidenceChars;

    private static LocatorField<object> Classify(Variant variant)
    {
        switch (variant.Type)
        {
            case Variant.VtEmpty or Variant.VtNull or Variant.VtVoid:
                return NodeProperties.AbsentRead;
            case Variant.VtBool:
                return LocatorField<object>.Known(variant.Flag != 0);
            case Variant.VtI4 or Variant.VtInt:
                return LocatorField<object>.Known(variant.Number);
            case Variant.VtBstr:
                var text = variant.Pointer == IntPtr.Zero ? "" : System.Runtime.InteropServices.Marshal.PtrToStringBSTR(variant.Pointer);
                return ExceedsEvidenceBound(text) ? NodeProperties.UnknownRead : LocatorField<object>.Known(text);
            case Variant.VtUnknown:
                return variant.Pointer != IntPtr.Zero && variant.Pointer == NotSupported()
                    ? NodeProperties.AbsentRead
                    : NodeProperties.UnknownRead;
            default:
                return NodeProperties.UnknownRead;
        }
    }

    private static IntPtr NotSupported()
    {
        if (_notSupported == IntPtr.Zero && Calls.UiaGetReservedNotSupportedValue(out var sentinel) >= 0)
        {
            _notSupported = sentinel;
        }

        return _notSupported;
    }

    private LocatorField<Rect> ReadBounds(int slot)
    {
        if (_pointer == IntPtr.Zero || Calls.CallRectOut(_pointer, slot, out var edges) < 0)
        {
            return LocatorField<Rect>.Unknown();
        }

        var width = Math.Max((long)edges.right - edges.left, 0);
        var height = Math.Max((long)edges.bottom - edges.top, 0);
        return LocatorField<Rect>.Known(new Rect(edges.left, edges.top, width, height));
    }

    private LocatorField<object> ReadProperty(int slot, int propertyId)
    {
        if (_pointer == IntPtr.Zero)
        {
            return NodeProperties.UnknownRead;
        }

        var hr = Calls.CallVariantOut(_pointer, slot, propertyId, out var variant);
        try
        {
            return hr < 0 ? NodeProperties.UnknownRead : Classify(variant);
        }
        finally
        {
            Calls.VariantClear(ref variant);
        }
    }

    private LocatorField<string> Text(int slot)
    {
        if (_pointer == IntPtr.Zero || Calls.CallStringOut(_pointer, slot, out var text) < 0)
        {
            return LocatorField<string>.Unknown();
        }

        text ??= "";
        return ExceedsEvidenceBound(text) ? LocatorField<string>.Unknown() : LocatorField<string>.Known(text);
    }

    private LocatorField<int> Number(int slot)
    {
        if (_pointer == IntPtr.Zero || Calls.CallIntOut(_pointer, slot, out var value) < 0)
        {
            return LocatorField<int>.Unknown();
        }

        return LocatorField<int>.Known(value);
    }

    private LocatorField<bool> Flag(int slot)
    {
        if (_pointer == IntPtr.Zero || Calls.CallIntOut(_pointer, slot, out var value) < 0)
        {
            return LocatorField<bool>.Unknown();
        }

        return LocatorField<bool>.Known(value != 0);
    }
}
