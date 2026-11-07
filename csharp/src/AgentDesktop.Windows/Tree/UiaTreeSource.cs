using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>The live tree source. Each node's children come from one
/// FindAllBuildCache(TreeScope_Children, TrueCondition) call that returns them with the
/// whole read set already cached, so a node costs one cross-process call and its property
/// reads cost none. The true condition matches the raw view, so the walk sees the same
/// children the raw view walker the Rust adapter uses would. The cache request is built once
/// per walk: element scope, a true tree filter so raw-view elements are cached too, and
/// the default full element mode, so a walked element can still be acted on.</summary>
internal sealed class UiaTreeSource : TreeSource<Element>, IDisposable
{
    private const string ChromiumWindowClass = "Chrome_WidgetWin_1";

    /// <summary>E_POINTER, for a zero element that is refused rather than called through.</summary>
    internal const int InvalidPointer = unchecked((int)0x8000_4003);

    private static readonly int[] TypedReads =
    {
        PropertyIds.Name,
        PropertyIds.AutomationId,
        PropertyIds.ControlType,
        PropertyIds.ClassName,
        PropertyIds.FrameworkId,
        PropertyIds.IsEnabled,
        PropertyIds.IsOffscreen,
        PropertyIds.HasKeyboardFocus,
        PropertyIds.IsKeyboardFocusable,
    };

    private static readonly int[] VariantReads =
    {
        PropertyIds.IsPassword,
        PropertyIds.IsRequiredForForm,
        PropertyIds.IsInvokePatternAvailable,
        PropertyIds.IsTogglePatternAvailable,
        PropertyIds.IsExpandCollapsePatternAvailable,
        PropertyIds.IsSelectionItemPatternAvailable,
        PropertyIds.IsSelectionPatternAvailable,
        PropertyIds.IsValuePatternAvailable,
        PropertyIds.IsRangeValuePatternAvailable,
        PropertyIds.IsScrollPatternAvailable,
        PropertyIds.IsScrollItemPatternAvailable,
        PropertyIds.IsWindowPatternAvailable,
        PropertyIds.IsGridItemPatternAvailable,
        PropertyIds.IsTableItemPatternAvailable,
        PropertyIds.IsDialog,
        PropertyIds.ValueValue,
        PropertyIds.ValueIsReadOnly,
        PropertyIds.RangeValueIsReadOnly,
        PropertyIds.SelectionCanSelectMultiple,
        PropertyIds.SelectionItemIsSelected,
        PropertyIds.ExpandCollapseState,
        PropertyIds.ToggleState,
        PropertyIds.WindowIsModal,
        PropertyIds.LegacyState,
        PropertyIds.LegacyDefaultAction,
    };

    private readonly bool _includeBounds;

    private IntPtr _condition;

    private IntPtr _request;

    private bool _chromiumProvenance;

    private UiaTreeSource(IntPtr condition, bool includeBounds)
    {
        _condition = condition;
        _includeBounds = includeBounds;
    }

    /// <summary>Builds the condition and cache request one walk uses. Bounds join the read
    /// set only when the caller asked for them.</summary>
    internal static UiaTreeSource Create(Client client, bool includeBounds)
    {
        var hr = client.TrueCondition(out var condition);
        if (hr < 0 || condition == IntPtr.Zero)
        {
            Calls.Release(condition);
            throw UiaFailure.Error(hr, "create a true condition");
        }

        var source = new UiaTreeSource(condition, includeBounds);
        try
        {
            source.BuildCacheRequest(client);
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Fetches the read set onto the root. An element from ElementFromHandle carries
    /// no cache, so without this the root alone would read unknown for every property. Also
    /// decides Chromium provenance from the root's class, which gates the wrapper skip.</summary>
    internal Element PrepareRoot(Element root)
    {
        var updated = IntPtr.Zero;
        var hr = root.Pointer == IntPtr.Zero
            ? InvalidPointer
            : Calls.CallOutPointerWithArg(root.Pointer, Slots.ElementBuildUpdatedCache, _request, out updated);
        if (hr < 0 || updated == IntPtr.Zero)
        {
            Calls.Release(updated);
            throw UiaFailure.Error(hr, "prefetch the properties of a root element");
        }

        var prepared = new Element(updated);
        var className = prepared.ClassName();
        _chromiumProvenance = className.Kind == LocatorFieldKind.Known && className.Value!.Trim() == ChromiumWindowClass;
        return prepared;
    }

    public NodeProperties Read(Element node)
    {
        var reads = new Dictionary<int, LocatorField<object>>(TypedReads.Length + VariantReads.Length + 1)
        {
            [PropertyIds.Name] = NodeProperties.From(node.Name()),
            [PropertyIds.AutomationId] = NodeProperties.From(node.AutomationId()),
            [PropertyIds.ControlType] = NodeProperties.From(node.ControlType()),
            [PropertyIds.ClassName] = NodeProperties.From(node.ClassName()),
            [PropertyIds.FrameworkId] = NodeProperties.From(node.FrameworkId()),
            [PropertyIds.IsEnabled] = NodeProperties.From(node.IsEnabled()),
            [PropertyIds.IsOffscreen] = NodeProperties.From(node.IsOffscreen()),
            [PropertyIds.HasKeyboardFocus] = NodeProperties.From(node.HasKeyboardFocus()),
            [PropertyIds.IsKeyboardFocusable] = NodeProperties.From(node.IsKeyboardFocusable()),
        };
        if (_includeBounds)
        {
            reads[PropertyIds.BoundingRectangle] = NodeProperties.From(node.BoundingRectangle());
        }

        foreach (var property in VariantReads)
        {
            reads[property] = node.Property(property);
        }

        return new NodeProperties(reads);
    }

    /// <summary>A count-only read uses a plain FindAll, so children that are only counted do
    /// not pay for a read set nobody looks at.</summary>
    public int Children(Element node, bool countOnly, List<Element> into)
    {
        if (node.Pointer == IntPtr.Zero)
        {
            return InvalidPointer;
        }

        var hr = countOnly
            ? Calls.CallOutPointerWithIntAndPointer(node.Pointer, Slots.ElementFindAll, Slots.TreeScopeChildren, _condition, out var found)
            : Calls.FindAllBuildCache(node.Pointer, Slots.TreeScopeChildren, _condition, _request, out found);
        if (hr < 0 || found == IntPtr.Zero)
        {
            Calls.Release(found);
            return hr;
        }

        try
        {
            return AppendElements(found, into);
        }
        finally
        {
            Calls.Release(found);
        }
    }

    public bool IsWebWrapper(NodeProperties properties) => _chromiumProvenance && WebWrapper.Matches(properties);

    public void Dispose()
    {
        Calls.Release(_request);
        Calls.Release(_condition);
        _request = IntPtr.Zero;
        _condition = IntPtr.Zero;
    }

    private static int AppendElements(IntPtr array, List<Element> into)
    {
        var hr = Calls.CallIntOut(array, Slots.ElementArrayLength, out var length);
        for (var index = 0; hr >= 0 && index < length; index++)
        {
            hr = Calls.CallOutPointerWithIntArg(array, Slots.ElementArrayElement, index, out var child);
            if (hr >= 0 && child != IntPtr.Zero)
            {
                into.Add(new Element(child));
            }
        }

        return hr;
    }

    private void BuildCacheRequest(Client client)
    {
        var hr = client.CreateCacheRequest(out var request);
        if (hr < 0 || request == IntPtr.Zero)
        {
            Calls.Release(request);
            throw UiaFailure.Error(hr, "create a cache request");
        }

        _request = request;
        Check(Calls.CallWithInt(request, Slots.CacheRequestPutTreeScope, Slots.TreeScopeElement), "set the cache tree scope");
        Check(Calls.CallWithPointer(request, Slots.CacheRequestPutTreeFilter, _condition), "set the cache tree filter");
        var properties = _includeBounds
            ? TypedReads.Concat(VariantReads).Append(PropertyIds.BoundingRectangle)
            : TypedReads.Concat(VariantReads);
        foreach (var property in properties)
        {
            Check(Calls.CallWithInt(request, Slots.CacheRequestAddProperty, property), "add a property to the cache request");
        }
    }

    private static void Check(int hr, string context)
    {
        if (hr < 0)
        {
            throw UiaFailure.Error(hr, context);
        }
    }
}
