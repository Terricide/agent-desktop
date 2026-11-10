using global::System.Collections.Generic;
using global::System.Linq;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Windows.System.Window;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// The immersive family's resolver: the half of the shell-surface seam the Win32 top-level walk cannot reach (A26-1).
/// It walks the UIA root's children and matches the first child whose class, hosting shell process, landmark and cloak
/// state name the kind's surface. The landmark search's failure classification lives beside it, because "search failed"
/// must never read as "surface closed".
/// </summary>
internal static class ShellSurfaceImmersive
{
    private static readonly string[] DiagnosticHostImages =
    {
        "shellexperiencehost",
        "searchhost",
        "searchui",
        "searchapp",
        "startmenuexperiencehost",
        "shellhost",
    };

    /// <summary>
    /// Walks the UIA root's children - never the Win32 top-level walk, which does not yield the immersive surfaces (A26-1) -
    /// and matches the first child whose class, hosting shell process, landmark and cloak state name this kind's surface.
    /// The landmark is what keeps two kinds hosted by the same shell process from resolving each other's surface: on this
    /// build the Start overlay and the Action Center are both <c>ShellExperienceHost</c> children of the root, so neither
    /// the class nor the host image alone is an identity, and a sibling kind's live surface must simply not match - a read
    /// may run while the sibling is up, so absence is the correct answer for it. A child whose own attributes fail to read
    /// is skipped: the root's population changes under the read, and one raced child must not fail the walk for every other
    /// caller. A landmark search that could not run is the exception and surfaces instead: resolving "no surface" from a
    /// search fault is the answer a close path would skip a dismissal on.
    /// </summary>
    /// <remarks>
    /// The open path needs one more distinction the read cannot make - whether a raise presented a surface whose tree
    /// matches no landmark at all - and it carries its own pre-raise witness to attribute the child to its raise
    /// (<c>ShellSurfaceOpen.PollUntilObserved</c> and <see cref="ForeignShapeError"/>).
    /// </remarks>
    internal static WindowInfo? ResolveImmersive(string expectedClass, IReadOnlyList<string> hostImages, IReadOnlyList<string> landmarks)
    {
        var client = AcquireClient();
        foreach (var child in DesktopRootChildren(client))
        {
            var info = ImmersiveCandidate(client, child, expectedClass, hostImages, landmarks);
            if (info is not null)
            {
                return info;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether a process image is one of the shell hosts a surface row names.
    /// </summary>
    internal static bool IsHostImage(string image, IReadOnlyList<string> hostImages)
    {
        var stem = image.EndsWith(".exe", global::System.StringComparison.Ordinal)
            ? image[..^4]
            : image;
        return hostImages.Any(host => string.Equals(host, stem, global::System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether this top-level window is the row's own surface: one of its shell hosts owns it and its tree carries one of
    /// its landmarks. The host alone is not an identity - Start and the Action Center can share one - and Start can hand its
    /// foreground to a search window outside the overlay, so the overlay's handle alone is not one either.
    /// </summary>
    internal static bool WindowIsSurface(nint handle, IReadOnlyList<string> hostImages, IReadOnlyList<string> landmarks)
    {
        var owner = WindowIdentity.LiveWindowOwner(handle);
        var image = owner is null ? null : ProcessIdentityCheck.ProcessImageName(owner);
        if (image is null || !IsHostImage(image, hostImages))
        {
            return false;
        }
        var client = AcquireClient();
        var hr = client.ElementFromHandle(handle, out var pointer);
        if (hr < 0 || pointer == IntPtr.Zero)
        {
            return false;
        }
        using var element = new Element(pointer);
        return CarriesLandmark(client, element, landmarks);
    }

    /// <summary>
    /// Whether a presented surface of this kind's class and host, raised by the caller and absent from the pre-raise witness,
    /// matches none of the kind's landmarks - the raise presented a shape this build's measurements do not cover, and the
    /// caller resolves that as a named refusal rather than as absence, so a caller is told the shape did not match instead of
    /// that no surface exists. A child already in the witness is a sibling kind's live surface or a dismissed one's lingering
    /// window: it belongs to no raise and never answers this question.
    /// </summary>
    internal static bool RaisePresentedForeignShape(Client client, IReadOnlyList<nint> preRaiseChildren, IReadOnlyList<string> landmarks)
    {
        foreach (var child in DesktopRootChildren(client))
        {
            var host = ShellHostChild(child, ShellSurfaceKinds.CoreWindowClass, DiagnosticHostImages, fallbackToLiveOwner: false);
            if (host is null || preRaiseChildren.Contains(host.Value.Handle))
            {
                continue;
            }
            if (!CarriesLandmark(client, child, landmarks))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The handles of the root children a raise could present through - the CoreWindow class owned by any shell host image
    /// and not cloaked - captured before the raise so the poll can attribute a new foreign-shape child to it rather than to a
    /// sibling kind's live surface. The host set is the broad diagnostic one: newer shells route these surfaces through host
    /// images this build's rows never named.
    /// </summary>
    internal static List<nint> WitnessImmersiveChildren()
    {
        var client = AcquireClient();
        var handles = new List<nint>();
        foreach (var child in DesktopRootChildren(client))
        {
            var host = ShellHostChild(child, ShellSurfaceKinds.CoreWindowClass, DiagnosticHostImages, fallbackToLiveOwner: false);
            if (host is not null)
            {
                handles.Add(host.Value.Handle);
            }
        }
        return handles;
    }

    /// <summary>
    /// The named refusal for a raise that presented a surface whose tree matches none of the kind's landmarks: the shell
    /// answered, the shape is not the measured one, and "no surface found" would send a caller to open a surface that is
    /// already on screen. The detail names the build and the landmarks the shape was matched against, per the error table's
    /// opened-but-foreign-shape row.
    /// </summary>
    internal static AdapterError ForeignShapeError(IReadOnlyList<string> landmarks)
    {
        var details = new global::System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = "shell_surface_foreign_shape",
        };
        return new AdapterError(
                ErrorCode.PlatformNotSupported,
                "the shell presented a surface of this kind whose tree does not match the landmarks this build's adapter measures")
            .WithPlatformDetail(
                "this Windows build presents a different surface shape; the landmarks matched were: "
                + string.Join(", ", landmarks))
            .WithDetails(details)
            .WithDisposition(Disposition.DeliveredUnverified());
    }

    /// <remarks>
    /// SEAM: ShellSurfaceImmersive.cs:AcquireClient needs a shared UI Automation client. `Client`
    /// has no shared instance: its only constructor is `Create(out Client?)`, which reports the
    /// HRESULT that prevented creation, so a client is never held. Until a shared client is ported,
    /// this path refuses by name.
    /// </remarks>
    private static Client AcquireClient() =>
        throw AdapterError.NotSupported("acquire the UI Automation client");

    /// <remarks>
    /// SEAM: ShellSurfaceImmersive.cs:DesktopRootChildren needs `Client.DesktopRootChildren`, the
    /// root's child elements. `Client` enumerates no subtree, so the immersive walk has no children
    /// to match. Until that read is ported, this path refuses by name.
    /// </remarks>
    private static IReadOnlyList<Element> DesktopRootChildren(Client client) =>
        throw AdapterError.NotSupported("walk the UI Automation root's children");

    private static T Narrowed<T>(global::System.Func<T> read)
    {
        try
        {
            return read();
        }
        catch (AdapterError error)
        {
            throw ListingRetry.NarrowToPermittedCodes(error);
        }
    }

    private static WindowInfo? ImmersiveCandidate(
        Client client,
        Element child,
        string expectedClass,
        IReadOnlyList<string> hostImages,
        IReadOnlyList<string> landmarks)
    {
        var host = ShellHostChild(child, expectedClass, hostImages, fallbackToLiveOwner: true);
        if (host is null)
        {
            return null;
        }
        if (!CarriesLandmark(client, child, landmarks))
        {
            return null;
        }
        return WindowInfoFromSurface(child, host.Value.Handle, host.Value.Pid, host.Value.Image);
    }

    /// <summary>
    /// The identity a shell-host child names once its class, live handle, cloak state, owning process and host image all
    /// agree with the caller's kind - the common prefix every immersive-family child filter shares, up to but not including
    /// the kind-specific landmark decision.
    /// </summary>
    /// <remarks>
    /// <paramref name="fallbackToLiveOwner"/> distinguishes the two shapes this walk needs: the resolve path falls back to
    /// the live window owner when the element's own PID read fails, because a raced read must not cost a caller the surface
    /// it is resolving; the foreign-shape diagnosis and witness walks have no such requirement and skip a child whose PID
    /// they cannot read.
    /// SEAM: ShellSurfaceImmersive.cs:ShellHostChild needs `Element.NativeWindowHandle` and
    /// `Element.ProcessId`. `Element` reads neither: it has no handle read, so the cloak test and the handle the
    /// identity returns are unavailable, and no owning process is read from the element. Until those reads are ported,
    /// this path refuses by name.
    /// </remarks>
    private static (nint Handle, ProcessId Pid, string Image)? ShellHostChild(
        Element child,
        string expectedClass,
        IReadOnlyList<string> hostImages,
        bool fallbackToLiveOwner)
    {
        var classname = child.ClassName().ToNullableText();
        if (classname is null || !string.Equals(classname, expectedClass, global::System.StringComparison.Ordinal))
        {
            return null;
        }
        throw AdapterError.NotSupported("resolve a shell-host child surface");
    }

    /// <summary>
    /// Whether the candidate's subtree carries one of the kind's landmark <c>AutomationId</c>s. A landmark that reads as
    /// absent is absence, not failure, and a subtree that raced away between the root scan and this read simply does not
    /// match. A search that could not run - a transport timeout, a denied read - is a fault that surfaces: reading it as
    /// absence is what lets a presented surface be reported closed, and a close on that answer is skipped without ever
    /// dismissing.
    /// </summary>
    private static bool CarriesLandmark(Client client, Element candidate, IReadOnlyList<string> landmarks)
    {
        const string context = "search an immersive surface candidate for its landmark";
        foreach (var landmark in landmarks)
        {
            if (LandmarkSearchAnswer(candidate, landmark, context))
            {
                return true;
            }
        }
        return false;
    }

    /// <remarks>
    /// SEAM: ShellSurfaceImmersive.cs:LandmarkSearchAnswer needs `Element.FindFirstByAutomationId`, a
    /// subtree search by `AutomationId`. `Element` searches nothing, so a landmark cannot be matched
    /// against a candidate. Until that search is ported, this path refuses by name.
    /// </remarks>
    private static bool LandmarkSearchAnswer(Element candidate, string landmark, string context) =>
        throw AdapterError.NotSupported("search an immersive surface candidate for its landmark");

    /// <summary>
    /// Builds the surface identity with the same field shapes the window listing emits: <c>w-&lt;hwnd&gt;</c> id, the owning
    /// process's image name and pid, live rect and window state read off the same handle.
    /// </summary>
    private static WindowInfo WindowInfoFromSurface(Element element, nint handle, ProcessId pid, string image)
    {
        return new WindowInfo
        {
            Id = WindowOps.WindowId(handle),
            Title = element.Name().ToNullableText() ?? string.Empty,
            App = image,
            Pid = pid,
            ProcessInstance = ProcessTimes.TokenForPid(pid.Value),
            Bounds = WindowEnum.WindowRect(handle),
            State = WindowOps.BuildWindowState(
                WindowOps.IsForegroundWindow(handle),
                Interop.IsIconic(handle) != 0,
                Interop.IsWindowVisible(handle) != 0),
        };
    }
}
