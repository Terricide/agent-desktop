using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;
using PropertyIds = AgentDesktop.Windows.Uia.PropertyIds;

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

    private static readonly LocatorField<object> AbsentRead = LocatorField<object>.Absent();

    private static readonly LocatorField<object> UnknownRead = LocatorField<object>.Unknown();

    private static readonly Guid ElementInterfaceId = new("D6A8AD98-039F-483A-A8C9-954C37984804");

    private IntPtr _pointer;

    private ProcessPayload? _verified;

    internal Element(IntPtr pointer) => _pointer = pointer;

    internal IntPtr Pointer => _pointer;

    /// <summary>The process this element's identity was verified against at capture, when a ref stamped one.</summary>
    internal ProcessPayload? VerifiedProcess => _verified;

    /// <summary>Stamps the verified process on this element and returns it, so the stamped element hands on as is.</summary>
    internal Element WithVerifiedProcess(uint pid, string token)
    {
        _verified = new ProcessPayload(pid, token);
        return this;
    }

    /// <summary>Wraps this element in a native handle. Disposing the handle disposes the element.</summary>
    internal NativeHandle IntoNativeHandle() => NativeHandle.New(this);

    internal LocatorField<string> Name() => Text(Slots.ElementGetCachedName);

    internal LocatorField<string> AutomationId() => Text(Slots.ElementGetCachedAutomationId);

    internal LocatorField<string> ClassName() => Text(Slots.ElementGetCachedClassName);

    internal LocatorField<int> ControlType() => Number(Slots.ElementGetCachedControlType);

    internal LocatorField<bool> IsEnabled() => Flag(Slots.ElementGetCachedIsEnabled);

    internal LocatorField<bool> IsOffscreen() => Flag(Slots.ElementGetCachedIsOffscreen);

    internal LocatorField<bool> HasKeyboardFocus() => Flag(Slots.ElementGetCachedHasKeyboardFocus);

    internal LocatorField<bool> IsKeyboardFocusable() => Flag(Slots.ElementGetCachedIsKeyboardFocusable);

    internal LocatorField<string> CurrentName() => Text(Slots.ElementGetName);

    internal LocatorField<string> CurrentAutomationId() => Text(Slots.ElementGetAutomationId);

    internal LocatorField<string> CurrentClassName() => Text(Slots.ElementGetClassName);

    /// <summary>The UI framework that produced the element. Gates a framework-specific search so a
    /// classic Win32 control of the same control type cannot match.</summary>
    internal LocatorField<string> CurrentFrameworkId() => Text(Slots.ElementGetFrameworkId);

    internal LocatorField<int> CurrentControlType() => Number(Slots.ElementGetControlType);

    internal LocatorField<bool> CurrentIsEnabled() => Flag(Slots.ElementGetIsEnabled);

    internal LocatorField<bool> CurrentIsOffscreen() => Flag(Slots.ElementGetIsOffscreen);

    internal LocatorField<bool> CurrentHasKeyboardFocus() => Flag(Slots.ElementGetHasKeyboardFocus);

    internal LocatorField<bool> CurrentIsKeyboardFocusable() => Flag(Slots.ElementGetIsKeyboardFocusable);

    /// <summary>Screen bounds in device pixels from the walk cache. A negative extent reads as zero.</summary>
    internal LocatorField<Rect> BoundingRectangle() => ReadBounds(Slots.ElementGetCachedBoundingRectangle);

    /// <summary>Live owning process id. A provider that reports 0 is read as no answer, not as
    /// process zero, so a physical input target with no readable owner is refused.</summary>
    internal int? CurrentProcessId()
    {
        if (_pointer == IntPtr.Zero || Calls.CallIntOut(_pointer, Slots.ElementGetProcessId, out var pid) < 0)
        {
            return null;
        }

        return pid == 0 ? null : pid;
    }

    /// <summary>Live screen bounds in device pixels.</summary>
    internal LocatorField<Rect> CurrentBoundingRectangle() => ReadBounds(Slots.ElementGetBoundingRectangle);

    /// <summary>Any cached property by id, classified the way the Rust adapter classifies a
    /// VARIANT: an empty, null or void VARIANT and the not-supported sentinel are absent; a
    /// BOOL, an integer and a string are known; any other shape is unknown.</summary>
    internal LocatorField<object> Property(int propertyId) => ReadProperty(Slots.ElementGetCachedPropertyValue, propertyId);

    /// <summary>Any live property by id, queried directly from the provider.</summary>
    internal LocatorField<object> CurrentProperty(int propertyId) => ReadProperty(Slots.ElementGetCurrentPropertyValue, propertyId);

    /// <summary>The HWND of the window this element belongs to. Null answers nothing: the property
    /// would not read, or reported a shape that is not an integer. A zero handle is a real answer
    /// meaning the element has no window of its own, which is the normal shape for a control
    /// inside a window, so a caller must distinguish zero from null.</summary>
    internal int? CurrentNativeWindowHandle()
    {
        var field = CurrentProperty(PropertyIds.NativeWindowHandle);
        return field.IsKnown() && field.Value is int handle ? handle : null;
    }

    /// <summary>The element a live element-valued property names, cast from the variant's
    /// IUnknown. A property that failed to read, a zero pointer, a variant that is not an
    /// unknown, and an unknown that is not an element all return null, so a caller cannot tell
    /// "no target" from "the read failed". The returned element owns its pointer.</summary>
    internal Element? CurrentElementOf(TreeProperty property) => ElementOf(Slots.ElementGetCurrentPropertyValue, PropertyUia.ToUiaProperty(property));

    /// <summary>The element a cached element-valued property names. The target's own cached reads
    /// are not the ones a label request needs, which is why the label relation's target is read
    /// live (A15-3).</summary>
    internal Element? CachedElementOf(TreeProperty property) => ElementOf(Slots.ElementGetCachedPropertyValue, PropertyUia.ToUiaProperty(property));

    private Element? ElementOf(int slot, int propertyId)
    {
        if (_pointer == IntPtr.Zero)
        {
            return null;
        }

        var hr = Calls.CallVariantOut(_pointer, slot, propertyId, out var variant);
        try
        {
            if (hr < 0 || variant.Type != Variant.VtUnknown || variant.Pointer == IntPtr.Zero)
            {
                return null;
            }

            var query = global::System.Runtime.InteropServices.Marshal.QueryInterface(
                variant.Pointer, in ElementInterfaceId, out var target);
            return query >= 0 && target != IntPtr.Zero ? new Element(target) : null;
        }
        finally
        {
            Calls.VariantClear(ref variant);
        }
    }

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

    /// <summary>Binds a pattern interface to this element, the C# shape of Rust
    /// `get_pattern::<T>()`. A pattern the element does not expose throws `UiaException`
    /// carrying sentinel 0; a call the provider refused carries its HRESULT. The bound
    /// pattern owns its pointer and releases it on disposal.</summary>
    internal T GetPattern<T>()
        where T : class => PatternBinding.FromElement<T>(this);

    /// <summary>The Toggle pattern, the one the toggle command writes through.</summary>
    internal ITogglePattern GetTogglePattern() => GetPattern<ITogglePattern>();

    /// <summary>The Scroll pattern, the surface the scroll ladder drives.</summary>
    internal Scroll.IScrollPattern GetScrollPattern() => GetPattern<Scroll.IScrollPattern>();

    /// <summary>Resolves this element's `LabeledBy` relation. The caller must release a non-zero
    /// target pointer. A relation the provider does not publish returns zero, which is the
    /// ordinary case on a Win32 fixture, not a failure.</summary>
    internal IntPtr LabeledByTarget(bool cached)
    {
        if (_pointer == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var slot = cached ? Slots.ElementGetCachedLabeledBy : Slots.ElementGetCurrentLabeledBy;
        return Calls.CallOutPointer(_pointer, slot, out var target) < 0 ? IntPtr.Zero : target;
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
                return AbsentRead;
            case Variant.VtBool:
                return LocatorField<object>.Known(variant.Flag != 0);
            case Variant.VtI4 or Variant.VtInt:
                return LocatorField<object>.Known(variant.Number);
            case Variant.VtBstr:
                var text = variant.Pointer == IntPtr.Zero ? "" : global::System.Runtime.InteropServices.Marshal.PtrToStringBSTR(variant.Pointer);
                return ExceedsEvidenceBound(text) ? UnknownRead : LocatorField<object>.Known(text);
            case Variant.VtUnknown:
                return variant.Pointer != IntPtr.Zero && variant.Pointer == NotSupported()
                    ? AbsentRead
                    : UnknownRead;
            default:
                return UnknownRead;
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
            return UnknownRead;
        }

        var hr = Calls.CallVariantOut(_pointer, slot, propertyId, out var variant);
        try
        {
            return hr < 0 ? UnknownRead : Classify(variant);
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
