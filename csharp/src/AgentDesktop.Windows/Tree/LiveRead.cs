using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// The shared single-element live read and its five projections (value, state, actions, element, bounds).
/// One read corroborates the verified process identity, reads the walk property set live, and projects it
/// through the walk's own vocabulary composition. The projections are functions over that one read.
/// </summary>
/// <remarks>
/// Process corroboration runs twice. The first check is cheap and rejects an already-dead handle before the
/// property fetch. The second check runs after the read and its completeness gate, and rejects a handle whose
/// process died during the read (A14-9, where a dead provider's reads still succeed). Only the second check
/// gates the result. The read's discarded errors are inspected for the vanished-element disposition before the
/// completeness gate sees them, so a target reporting a stale code settles as stale rather than surfacing as a
/// retryable, indefinitely repeated <c>AppUnresponsive</c>.
/// </remarks>
internal sealed record LiveRead
{
    /// <summary>The property set kept beside the projected evidence, because the state projection reads
    /// <c>IsEnabled</c> and <c>IsOffscreen</c> from it and the locator evidence does not carry them.</summary>
    internal required ElementProperties Properties { get; init; }

    internal required LocatorEvidence Evidence { get; init; }

    /// <summary>The shared single-element live read. Fails stale when the verified process token has moved on
    /// before or after the read, when the read reports the target vanished, and fails retryable
    /// <c>APP_UNRESPONSIVE</c> when an essential slot reads unknown. It never answers with a partial bundle that
    /// claims completeness.</summary>
    /// <remarks>
    /// The deadline-bounded property read is used rather than the unbounded one. The set is 43 properties wide,
    /// one cross-process call apiece, and the unbounded reader checked nothing between them, so a poll-style
    /// caller could overshoot its own timeout on every iteration. The bounded reader stops early and truncates
    /// the set; a truncated read still reaches the completeness gate, which is the established retryable shape.
    /// </remarks>
    internal static LiveRead ReadElement(NativeHandle handle, Deadline deadline)
    {
        var element = ElementFromHandle(handle);
        var outsideVisibleArea =
            (Minimized.OwningWindowMinimized(element, deadline) ?? false)
            || Minimized.OutsideScrollViewports(element, deadline);
        return ReadElementCore(
            element,
            deadline,
            outsideVisibleArea,
            new Seams(
                CorroborateVerifiedProcess,
                target => ReadLiveBounded(target, deadline),
                ReadLabel));
    }

    /// <summary>The read body, parameterized over process corroboration, the property read, and the label read.
    /// Production passes the real implementations; a test substitutes one to drive a specific failure.</summary>
    /// <remarks>
    /// The label read is skipped once the deadline has passed. A skipped label settles as
    /// <see cref="LabelOutcome"/> Failed, never Unlabelled, because Unlabelled would claim the provider publishes
    /// no label relation, which nothing measured. No slot the completeness gate inspects derives from the label.
    /// </remarks>
    internal static LiveRead ReadElementCore(Element element, Deadline deadline, bool owningWindowMinimized, Seams seams)
    {
        deadline.EnsureRemaining();
        seams.Corroborate(element);
        if (deadline.IsExpired())
        {
            throw deadline.TimeoutError();
        }

        var (properties, errors) = seams.Read(element);
        if (TargetReadReportsVanished(errors))
        {
            throw StaleReaderError();
        }

        if (owningWindowMinimized)
        {
            StampMinimizedOffscreen(properties);
        }

        var label = deadline.IsExpired() ? LabelOutcome.Failed : seams.ReadLabel(element);
        var vocabulary = WalkVocabulary(properties, label);
        var evidence = properties.ToLocatorEvidence(vocabulary);
        if (!LiveElementEvidence.EssentialEvidenceComplete(evidence))
        {
            throw LiveElementEvidence.Incomplete();
        }

        seams.Corroborate(element);
        return new LiveRead { Properties = properties, Evidence = evidence };
    }

    /// <summary>Corroborates the element's verified process token against the process's live generation, right
    /// now. Not memoized: a second call re-queries the OS, which lets the post-read call catch a process that
    /// died during the read.</summary>
    internal static void CorroborateVerifiedProcess(Element element)
    {
        var verified = element.VerifiedProcess;
        if (verified is { } process && !ProcessTimes.MatchesInstance(process.Pid, process.Token))
        {
            throw StaleReaderError();
        }
    }

    /// <summary>The value projection. A secure field's stored value is absent here by the read's own
    /// withholding, so nothing secure escapes.</summary>
    internal string? ToValue() => Evidence.Value.IsKnown() ? Evidence.Value.Value : null;

    /// <summary>The state projection. Provider flags include the minimized-window and viewport clipping
    /// corrections. <c>Hidden</c> stays unset because UI Automation has no separate flag.</summary>
    internal ElementState ToState()
    {
        var role = Evidence.Role.IsKnown()
            ? Evidence.Role.Value ?? throw LiveElementEvidence.Incomplete()
            : throw LiveElementEvidence.Incomplete();
        return new ElementState
        {
            Role = role,
            States = Evidence.States.Value ?? new List<string>(),
            Value = ToValue(),
            Enabled = Properties.Get(TreeProperty.IsEnabled).Flag(),
            Hidden = null,
            Offscreen = Properties.Get(TreeProperty.IsOffscreen).Flag(),
        };
    }

    /// <summary>The actions projection. A free projection; no pattern is invoked.</summary>
    internal List<string> ToActions() => LiveElementEvidence.KnownActions(Evidence.RefEvidence.AvailableActions);

    /// <summary>The bounds projection. An unknown bounds read fails the shared read first.</summary>
    internal Rect? ToBounds() => Evidence.RefEvidence.Bounds.IsKnown() ? Evidence.RefEvidence.Bounds.Value : null;

    /// <summary>The element projection: identity, state, bounds, and actions all from the one shared read.</summary>
    internal LiveElement ToElement()
    {
        var state = ToState();
        return new LiveElement
        {
            Identity = new LiveIdentity
            {
                Name = Evidence.Name,
                Description = Evidence.Description,
                Identifiers = Evidence.Identifiers,
            },
            State = state,
            StatesComplete = StatesAreComplete(state.Role),
            Bounds = ToBounds(),
            AvailableActions = ToActions(),
        };
    }

    /// <summary>Whether the control-state evidence the post-action verifier reads was observed. A toggleable role
    /// needs a readable <c>ToggleState</c>. The expand half needs a readable <c>ExpandCollapseState</c> wherever it
    /// applies.</summary>
    /// <remarks>
    /// The expand half applies to any element that advertises the pattern, and to an expandable role unless the
    /// provider answered that it has no <c>ExpandCollapse</c> pattern. A failed availability read on a
    /// non-expandable role leaves the half unapplied. A defaulted value is not an observation.
    /// </remarks>
    internal bool StatesAreComplete(string role)
    {
        var properties = Properties;
        var toggleObserved = !IsToggleableRole(role)
            || properties.GatedNumber(TreeProperty.ToggleState) is not null;
        const TreeProperty available = TreeProperty.ExpandCollapseAvailable;
        var expandApplies = properties.Get(available) is PropertyOutcome.Known
            ? properties.IsTrue(available)
            : IsExpandableRole(role);
        var expandObserved = !expandApplies
            || properties.GatedNumber(TreeProperty.ExpandCollapseState) is not null;
        return toggleObserved && expandObserved;
    }

    /// <summary>Reports whether the read's own discarded errors already settled the target as vanished. Keyed on
    /// the code alone: the vector only collects UI Automation property-read failures and the deadline's timeout
    /// stamp, and the only path that yields <see cref="ErrorCode.StaleRef"/> there names one family.</summary>
    private static bool TargetReadReportsVanished(IReadOnlyList<AdapterError> errors) =>
        errors.Any(error => error.Code == ErrorCode.StaleRef);

    /// <summary>The vanished-target answer, built through core's shared stale constructor. It carries no
    /// <c>retryable</c> key, so the error keeps the code's default rather than claiming a verdict it has not
    /// earned.</summary>
    private static AdapterError StaleReaderError() =>
        AdapterError.StaleBecause("Element became invalid while reading live state").WithDetails(
            new JsonObject
            {
                ["kind"] = "live_element_invalid",
                ["complete"] = true,
            });

    private static Element ElementFromHandle(NativeHandle handle) =>
        handle.Downcast<Element>() ?? throw new AdapterError(
                ErrorCode.InvalidArgs,
                "Native handle does not contain a Windows UI Automation element")
            .WithDetails(new JsonObject
            {
                ["kind"] = "invalid_native_handle",
                ["platform"] = "windows",
                ["empty"] = handle.IsNull(),
            });

    private static (ElementProperties Properties, List<AdapterError> Errors) ReadLiveBounded(
        Element element,
        Deadline deadline) => throw AdapterError.NotSupported("read_live_bounded");

    private static ResolvedVocabulary WalkVocabulary(ElementProperties properties, LabelOutcome label) =>
        throw AdapterError.NotSupported("walk_vocabulary");

    private static void StampMinimizedOffscreen(ElementProperties properties) =>
        throw AdapterError.NotSupported("stamp_minimized_offscreen");

    private static LabelOutcome ReadLabel(Element element) =>
        throw AdapterError.NotSupported("read_label");

    private static bool IsToggleableRole(string role) => role is "checkbox" or "switch" or "radiobutton";

    private static bool IsExpandableRole(string role) => role is "combobox" or "menubutton" or "treeitem" or "disclosure";

    /// <summary>The three operations <see cref="ReadElementCore"/> is parameterized over: process
    /// corroboration, the bounded property read, and the label read.</summary>
    internal sealed record Seams(
        Action<Element> Corroborate,
        Func<Element, (ElementProperties Properties, List<AdapterError> Errors)> Read,
        Func<Element, LabelOutcome> ReadLabel);
}
