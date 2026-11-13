using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;
using PropertyIds = AgentDesktop.Windows.Uia.PropertyIds;

namespace AgentDesktop.Windows.Tree;

/// <summary>The live tree source. A source that batches reads each node's children from one
/// FindAllBuildCache(TreeScope_Children, TrueCondition) call that returns them with the whole read set already
/// cached, so a node costs one cross-process call and its property reads cost none. A source that does not batch,
/// because its root's provider is client-side, reads every property live and lists children with plain FindAll.
/// The true condition matches the raw view, so the walk sees the same children the raw view walker the Rust adapter
/// uses would. A batching source builds its cache request once: element scope, a true tree filter so raw-view
/// elements are cached too, and the default full element mode, so a walked element can still be acted on.</summary>
internal sealed class UiaTreeSource : TreeSource<Element>, IDisposable
{
    private const string ChromiumWindowClass = "Chrome_WidgetWin_1";

    /// <summary>E_POINTER, for a zero element that is refused rather than called through.</summary>
    internal const int InvalidPointer = unchecked((int)0x8000_4003);

    [ThreadStatic]
    private static Client? _threadClient;

    private static readonly int[] TypedReads =
    {
        PropertyIds.Name,
        PropertyIds.AutomationId,
        PropertyIds.ControlType,
        PropertyIds.ClassName,
        PropertyIds.IsEnabled,
        PropertyIds.IsOffscreen,
        PropertyIds.HasKeyboardFocus,
        PropertyIds.IsKeyboardFocusable,
    };

    private static readonly int[] VariantReads =
    {
        PropertyIds.LocalizedControlType,
        PropertyIds.HelpText,
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
        PropertyIds.AriaRole,
        PropertyIds.FullDescription,
        PropertyIds.LabeledBy,
    };

    private readonly bool _includeBounds;

    private readonly bool _cached;

    private IntPtr _condition;

    private IntPtr _request;

    private IntPtr _walker;

    private bool _chromiumProvenance;

    private UiaTreeSource(bool includeBounds, bool cached)
    {
        _includeBounds = includeBounds;
        _cached = cached;
    }

    /// <summary>Builds the condition and, when the source batches, the cache request one walk uses. Bounds join
    /// the read set only when the caller asked for them. The caller keeps ownership of the client.</summary>
    internal static UiaTreeSource Create(Client client, bool includeBounds, bool cached = true) =>
        Build(client, includeBounds, cached);

    /// <summary>Builds the source a resolution walks from one root. The cache policy comes from the root's own
    /// provider and process id, as the Rust for_root decides it. The walk reads through this thread's client, which
    /// lives as long as the thread, as the Rust automation_client does, so the source never releases it.</summary>
    internal static UiaTreeSource ForRoot(Element root) =>
        Build(ThreadClient(), true, Cache.PolicyForRoot(root).Batches());

    private static Client ThreadClient()
    {
        if (_threadClient is { } existing)
        {
            return existing;
        }

        var hr = Client.Create(out var client);
        if (hr < 0 || client is null)
        {
            throw UiaFailure.Error(hr, "create a UI Automation client whose calls are bounded");
        }

        _threadClient = client;
        return client;
    }

    private static UiaTreeSource Build(Client client, bool includeBounds, bool cached)
    {
        var source = new UiaTreeSource(includeBounds, cached);
        try
        {
            source.Initialize(client);
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private void Initialize(Client client)
    {
        var hr = client.TrueCondition(out var condition);
        if (hr < 0 || condition == IntPtr.Zero)
        {
            Calls.Release(condition);
            throw UiaFailure.Error(hr, "create a true condition");
        }

        _condition = condition;
        var walkerHr = client.RawViewWalker(out var walker);
        if (walkerHr < 0 || walker == IntPtr.Zero)
        {
            Calls.Release(walker);
            throw UiaFailure.Error(walkerHr, "create the raw view walker");
        }

        _walker = walker;
        if (_cached)
        {
            BuildCacheRequest(client);
        }
    }

    /// <summary>Fetches the read set onto the root when the source batches, so the root reads from its cache like
    /// every node the walk retrieves. An uncached source returns the root as given and reads it live. Either way the
    /// root's class decides Chromium provenance, which gates the wrapper skip.</summary>
    internal Element PrepareRoot(Element root)
    {
        if (!_cached)
        {
            _chromiumProvenance = IsChromiumClass(root.CurrentClassName());
            return root;
        }

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
        _chromiumProvenance = IsChromiumClass(prepared.ClassName());
        return prepared;
    }

    public ElementProperties Read(Element node) => ReadWithFailures(node, out _);

    /// <summary>Reads the walk set from the element: its cache when the source batches, the provider otherwise. The
    /// count is the reads that failed, each of which leaves its slot unknown.</summary>
    private ElementProperties ReadWithFailures(Element node, out int failures)
    {
        List<(TreeProperty Property, PropertyOutcome Outcome)> reads = [];
        Put(reads, PropertyIds.Name, _cached ? node.Name() : node.CurrentName());
        Put(reads, PropertyIds.AutomationId, _cached ? node.AutomationId() : node.CurrentAutomationId());
        Put(reads, PropertyIds.ControlType, _cached ? node.ControlType() : node.CurrentControlType());
        Put(reads, PropertyIds.ClassName, _cached ? node.ClassName() : node.CurrentClassName());
        Put(reads, PropertyIds.IsEnabled, _cached ? node.IsEnabled() : node.CurrentIsEnabled());
        Put(reads, PropertyIds.IsOffscreen, _cached ? node.IsOffscreen() : node.CurrentIsOffscreen());
        Put(reads, PropertyIds.HasKeyboardFocus, _cached ? node.HasKeyboardFocus() : node.CurrentHasKeyboardFocus());
        Put(reads, PropertyIds.IsKeyboardFocusable, _cached ? node.IsKeyboardFocusable() : node.CurrentIsKeyboardFocusable());
        if (_includeBounds)
        {
            Put(reads, PropertyIds.BoundingRectangle, _cached ? node.BoundingRectangle() : node.CurrentBoundingRectangle());
        }

        foreach (var property in VariantReads)
        {
            Put(reads, property, _cached ? node.Property(property) : node.CurrentProperty(property));
        }

        failures = reads.Count(read => read.Outcome.IsUnknown());
        return ElementProperties.FromReads(reads);
    }

    private static void Put<T>(List<(TreeProperty Property, PropertyOutcome Outcome)> reads, int uiaProperty, LocatorField<T> field)
        where T : notnull
    {
        if (PropertyUia.ToTreeProperty(uiaProperty) is { } property)
        {
            reads.Add((property, PropertyUia.FromField(field)));
        }
    }

    /// <summary>The locator evidence one element yields: the walk's read set, the label relation, and the vocabulary
    /// the walk resolves from them. The third value counts the reads that failed.</summary>
    internal (ElementProperties Properties, LocatorEvidence Evidence, int ReadFailures) Evidence(Element node)
    {
        var properties = ReadWithFailures(node, out var failures);
        var vocabulary = Walker.WalkVocabulary(properties, ReadLabel(node));
        return (properties, properties.ToLocatorEvidence(vocabulary), failures);
    }

    public LabelOutcome ReadLabel(Element node) => ReadLabelOf(node, _cached);

    /// <summary>The label relation read live, for a caller that has no batching source. A live
    /// label target's own reads never ride a cache, which is the A15-3 finding.</summary>
    internal static LabelOutcome ReadLabelLive(Element node) => ReadLabelOf(node, false);

    /// <summary>Resolves an element's `LabeledBy` target and reads its name. **The relation rides the cache of a
    /// batching source; the target's own properties never do.** A15-3 measured that a cached
    /// element-returning property hands back a target whose cached reads fail for the very
    /// properties the same request asked for, so the target's name and `IsPassword` are read live, for the
    /// labelled nodes alone. On the WPF fixture that is two of forty-six. The relation costs nothing on an
    /// unlabelled node: it is one more slot on an element the walk already holds. The COM reads happen here; the
    /// policy on what they mean lives in `NameEvidenceFactory.LabelFromTarget`, which this function only calls.</summary>
    private static LabelOutcome ReadLabelOf(Element node, bool cached)
    {
        var (target, readFailed) = node.LabeledByTarget(cached);
        if (readFailed)
        {
            return LabelOutcome.Failed;
        }
        if (target == IntPtr.Zero)
        {
            return LabelOutcome.Unlabelled;
        }

        using var element = new Element(target);
        var secure = NameEvidenceFactory.SecureFromRead(element.CurrentProperty(PropertyIds.IsPassword));
        var name = element.CurrentName();
        return NameEvidenceFactory.LabelFromTarget(secure, name.Kind == LocatorFieldKind.Known ? name.Value : null);
    }

    /// <summary>Descends one level through the raw view. A node whose only children are content-view elements -
    /// a glyph carried by a `statictext` under a button, which is what Notepad's toolbar and status bar are made of -
    /// has no children in the control view, and a control-view count reports an empty subtree for a node that has
    /// children. The raw view walker is the surface that sees them, the same one the Rust adapter walks.
    /// A batching source builds the read set onto each element the walker hands out, so the walk's per-node read
    /// set rides the cache as it did under FindAllBuildCache.</summary>
    public (Element? Node, AutomationClassify.Failure? Failure) FirstChild(Element node) =>
        Step(node, Slots.WalkerGetFirstChild);

    public (Element? Node, AutomationClassify.Failure? Failure) NextSibling(Element node) =>
        Step(node, Slots.WalkerGetNextSibling);

    private (Element? Node, AutomationClassify.Failure? Failure) Step(Element node, int slot)
    {
        if (node.Pointer == IntPtr.Zero)
        {
            return (null, AutomationClassify.Failure.Hresult(InvalidPointer));
        }

        var hr = Calls.CallOutPointerWithArg(_walker, slot, node.Pointer, out var child);
        if (child != IntPtr.Zero)
        {
            var element = new Element(child);
            return _cached ? CacheOnto(element) : (element, null);
        }

        Calls.Release(child);
        if (hr >= 0 || hr == UiaFailure.ElementNotAvailable)
        {
            return (null, AutomationClassify.Failure.Sentinel(AutomationClassify.ErrNone));
        }

        return (null, AutomationClassify.Failure.Hresult(hr));
    }

    /// <summary>Fetches the walk's read set onto a walker element, so its properties cost no round trip. A cache
    /// read that fails keeps the walker element: its properties then read live, which is the slower path, not a
    /// lost node.</summary>
    private (Element? Node, AutomationClassify.Failure? Failure) CacheOnto(Element node)
    {
        var hr = Calls.CallOutPointerWithArg(node.Pointer, Slots.ElementBuildUpdatedCache, _request, out var updated);
        if (hr < 0 || updated == IntPtr.Zero)
        {
            Calls.Release(updated);
            return (node, null);
        }

        node.Dispose();
        return (new Element(updated), null);
    }

    public bool IsWebWrapper(ElementProperties properties) => _chromiumProvenance && WebWrapper.Matches(properties);

    public void Dispose()
    {
        Calls.Release(_request);
        Calls.Release(_condition);
        Calls.Release(_walker);
        _request = IntPtr.Zero;
        _condition = IntPtr.Zero;
        _walker = IntPtr.Zero;
    }

    private static bool IsChromiumClass(LocatorField<string> className) =>
        className.Kind == LocatorFieldKind.Known && className.Value!.Trim() == ChromiumWindowClass;

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
