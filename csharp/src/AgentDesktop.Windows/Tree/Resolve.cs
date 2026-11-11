using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;
using AgentDesktop.Windows.System.Window;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Tree.Resolve;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows;

/// <summary>Re-identifies a captured ref by its role and stable identity fields over one
/// bounded walk of the window it was captured from. Zero live matches is a stale ref, two
/// or more is an ambiguous target, and control values and bounds are never identity.</summary>
internal static class ElementResolve
{
    private const int ResolveDepth = 50;

    internal static NativeHandle ResolveStrict(RefEntry entry, Client client, Deadline deadline) =>
        RetryIncompleteUntil(deadline, () => ResolveStrictOnce(entry, client, deadline));

    /// <summary>Runs one resolution attempt inside the budget, retrying only an explicitly
    /// retryable, incomplete read. A provider's own transport timeout is one of those: it is a
    /// per-call timeout inside the operation's budget, not the budget running out. Pauses grow
    /// by doubling inside the deadline, so a struggling provider is not polled at the opening
    /// rate for the whole budget. Deadline exhaustion carries no retryable stamp, so it falls
    /// through to the caller as the stamped incomplete diagnosis, or as the bare timeout when
    /// nothing incomplete was seen.</summary>
    internal static NativeHandle RetryIncompleteUntil(Deadline deadline, Func<NativeHandle> operation)
    {
        AdapterError? lastIncomplete = null;
        var pause = ResolvePacing.FirstRetryPause;

        while (true)
        {
            if (deadline.IsExpired())
            {
                throw lastIncomplete is null
                    ? deadline.TimeoutError()
                    : MarkDeadlineElapsed(lastIncomplete);
            }

            try
            {
                return operation();
            }
            catch (AdapterError error) when (IsRetryableResolutionError(error))
            {
                lastIncomplete = error;
                ResolvePacing.SleepBeforeRetry(deadline, pause);
                pause = ResolvePacing.NextRetryPause(pause);
            }
            catch (AdapterError error) when (error.Code == ErrorCode.Timeout)
            {
                throw lastIncomplete is null ? error : MarkDeadlineElapsed(lastIncomplete);
            }
        }
    }

    /// <summary>Whether the adapter's own loop should retry an error: only an explicitly
    /// retryable, incomplete read. The granularity split between loop-settled and incomplete
    /// lives in the complete and retryable stamps every resolver error carries.</summary>
    private static bool IsRetryableResolutionError(AdapterError error) => error.IsTransientIncompleteRead;

    private static NativeHandle ResolveStrictOnce(RefEntry entry, Client client, Deadline deadline)
    {
        if (!entry.Capabilities.UniqueAtCapture || !entry.Identity.IsCaptureUnique)
        {
            var reason = entry.Identity.UniquenessReason ?? entry.UniquenessReason ?? "ref was not unique at capture";
            throw new AdapterError(ErrorCode.AmbiguousTarget, $"Ref cannot be resolved strictly: {reason}")
                .WithSuggestion("Take a fresh snapshot or narrow the target with a locator")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var handle = WindowEnum.HandleFor(entry.Scope.WindowId);
        if (handle == IntPtr.Zero)
        {
            throw StaleRef(entry);
        }

        var window = new WindowInfo
        {
            Id = entry.Scope.WindowId,
            App = entry.Process.Name,
            Pid = entry.Process.Pid,
            ProcessInstance = entry.Process.Instance,
        };

        VerifyWindowOrStale(handle, window, entry);

        using var source = UiaTreeSource.Create(client, false);
        using var rawRoot = ResolveWindowOrStale(client, handle, deadline, entry);
        var root = source.PrepareRoot(rawRoot);
        var matches = new List<Element>();
        try
        {
            Collect(source, root, entry.Identity, matches, 0, deadline);
            VerifyWindowOrStale(handle, window, entry);
        }
        catch
        {
            DisposeAll(matches);
            throw;
        }

        if (matches.Count == 0)
        {
            throw StaleRef(entry);
        }

        if (matches.Count > 1)
        {
            var count = matches.Count;
            DisposeAll(matches);
            throw new AdapterError(ErrorCode.AmbiguousTarget, "Multiple elements match the captured identity")
                .WithDetails(new JsonObject { ["matches"] = count })
                .WithSuggestion("Narrow the target with a locator")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return NativeHandle.New(matches[0]);
    }

    internal static RefEntryIdentity CandidateIdentity(ElementProperties properties, string windowKey)
    {
        var role = Roles.Resolve(properties);
        var fullDescription = properties.NonBlankText(TreeProperty.FullDescription);
        var helpText = properties.NonBlankText(TreeProperty.HelpText);
        return new RefEntryIdentity
        {
            Role = role,
            NativeId = properties.NonBlankText(TreeProperty.AutomationId),
            StableName = FullPredicate.StableNameField(role, NameField(properties), ValueField(properties)),
            Description = fullDescription ?? helpText,
            WindowKey = windowKey,
        };
    }

    /// <summary>The name as a field with certainty: a name UIA never returned is unread, a name
    /// it returned as empty means the element has no name, and a name it returned is the name.
    /// The first is an incomplete proof, so a ref captured with it cannot be re-identified.</summary>
    private static LocatorField<string> NameField(ElementProperties properties) =>
        properties.TextField(TreeProperty.Name).NonBlankField();

    /// <summary>The value as a field, read the way the node builder reads it, so the live read
    /// answers the stable-name rule with the same value the capture recorded.</summary>
    private static LocatorField<string> ValueField(ElementProperties properties) =>
        properties.Text(TreeProperty.Value).NonBlankToField();

    private static void Collect(
        UiaTreeSource source,
        Element element,
        RefEntryIdentity wanted,
        List<Element> matches,
        int depth,
        Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            element.Dispose();
            throw WindowRoot.DeadlineExceeded(deadline).Unsent();
        }

        if (depth > ResolveDepth)
        {
            element.Dispose();
            return;
        }

        var properties = source.Read(element);
        var candidate = CandidateIdentity(properties, wanted.WindowKey);
        var matchedSelf = wanted.MatchesCapture(candidate);

        var children = new List<Element>();
        if (source.Children(element, false, children) >= 0)
        {
            foreach (var child in children)
            {
                Collect(source, child, wanted, matches, depth + 1, deadline);
            }
        }
        else
        {
            DisposeAll(children);
        }

        if (matchedSelf)
        {
            matches.Add(element);
        }
        else
        {
            element.Dispose();
        }
    }

    private static void VerifyWindowOrStale(IntPtr handle, WindowInfo window, RefEntry entry)
    {
        try
        {
            WindowRoot.VerifyOwner(handle, window);
        }
        catch (AdapterError error) when (error.Code == ErrorCode.WindowNotFound)
        {
            throw StaleRef(entry);
        }
    }

    private static Element ResolveWindowOrStale(Client client, IntPtr handle, Deadline deadline, RefEntry entry)
    {
        try
        {
            return WindowRoot.Resolve(client, handle, deadline);
        }
        catch (AdapterError error) when (error.Code == ErrorCode.WindowNotFound)
        {
            throw StaleRef(entry);
        }
    }

    private static void DisposeAll(List<Element> elements)
    {
        foreach (var element in elements)
        {
            element.Dispose();
        }
    }

    internal static NativeHandle IntoVerifiedHandle(Element element, RefEntry entry) =>
        element.WithVerifiedProcess(entry.Process.Pid.Value, entry.Process.Instance ?? "").IntoNativeHandle();

    /// <summary>The window a ref re-identifies from. Fails closed: a ref with no window, a window that no longer
    /// verifies, or a window whose stored process has gone is refused before its tree is read.</summary>
    internal static Element ResolveWindowRoot(RefEntry entry, Deadline deadline)
    {
        var windowId = entry.Source.SourceWindowId ?? throw ResolveMatch.BuildStaleRefError(entry);
        var window = new WindowInfo
        {
            Id = windowId,
            Title = entry.Source.SourceWindowTitle ?? "",
            App = entry.Source.SourceApp ?? "",
            Pid = entry.Process.Pid,
            ProcessInstance = entry.Process.Instance,
        };
        var evidence = WindowIdentityEvidence.FromInfo(WindowOps.ParseHandle(windowId), window)
            ?? throw ResolveMatch.BuildStaleRefError(entry);
        try
        {
            evidence.VerifyStored();
        }
        catch (AdapterError)
        {
            throw VerifyStoredFailureError(entry, deadline);
        }

        return Surfaces.SurfaceRoot(new ObservationRoot.Window(window), entry.Source.Surface, deadline);
    }

    private static AdapterError VerifyStoredFailureError(RefEntry entry, Deadline deadline)
    {
        var instance = entry.Process.Instance;
        if (string.IsNullOrEmpty(instance))
        {
            return ResolveMatch.BuildStaleRefError(entry);
        }

        try
        {
            var state = ProcessStateProbe.ReadProcessState(new ProcessIdentity(entry.Process.Pid, instance), deadline);
            var exited = state.Kind is ProcessStateKind.Exited or ProcessStateKind.Crashed;
            return exited
                ? ResolveMatch.BuildOwningProcessExitedError(entry)
                : ResolveMatch.BuildStaleRefError(entry);
        }
        catch (AdapterError)
        {
            return ResolveMatch.BuildStaleRefError(entry);
        }
    }

    private static AdapterError StaleRef(RefEntry entry) =>
        new AdapterError(ErrorCode.StaleRef, "No live element matches the captured identity")
            .WithDetails(new JsonObject { ["native_id"] = entry.Identity.NativeId })
            .WithSuggestion("Take a fresh snapshot and re-identify the target")
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>Stamps the final incomplete diagnosis with the deadline running out, preserving
    /// the incomplete's own details rather than discarding them for a bare timeout.</summary>
    private static AdapterError MarkDeadlineElapsed(AdapterError error)
    {
        var parsed = error.Details is null ? null : JsonNode.Parse(error.Details);
        var details = parsed as JsonObject ?? new JsonObject();
        if (parsed is not null && parsed is not JsonObject)
        {
            details["evidence"] = parsed;
        }

        details["deadline_elapsed"] = true;
        return error.WithDetails(details);
    }
}
