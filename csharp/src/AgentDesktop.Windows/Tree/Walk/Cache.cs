using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;
using PropertyIds = AgentDesktop.Windows.Tree.Properties.PropertyIds;

namespace AgentDesktop.Windows.Tree;

/// <summary>Whether a subtree's reads are worth batching.
///
/// The decision is by provider class, never by node count. A node-count threshold is not
/// implementable here: the cache request must be built before the walk, and the count is known
/// only after it, so the arm would have no input at decision time. A6-2 also measured the only
/// available candidate threshold on the managed stack, where A2-4 measured the same window as 3
/// nodes against 26 on the COM stack the adapter uses.</summary>
internal enum CachePolicy
{
    /// <summary>Prefetch the walk property set with each element.</summary>
    Batch,

    /// <summary>Read live. A6-2 measured unconditional batching against an in-process client-side
    /// provider at 0.5763x overall and 0.436x on the element-finding step.</summary>
    Skip,
}

/// <summary>Chooses the walk's caching policy and builds the cache request the walk uses.
///
/// The namespace is Tree, not Tree.Walk: a namespace named Walk would collide with the generic
/// Walk type declared in Tree/Walk.cs.</summary>
internal static class Cache
{
    private const string ClientSideProviderModule = "uiautomationcore.dll";

    private const string PidMarker = "pid:";

    internal static bool Batches(this CachePolicy policy) => policy == CachePolicy.Batch;

    /// <summary>Chooses the policy from a root-level ProviderDescription read and the target
    /// element's own process id.
    ///
    /// A description that cannot be read leaves the policy at Batch: a wrong Skip costs a
    /// cross-process round trip per property on exactly the trees where batching pays, while a
    /// wrong Batch costs request setup on a tree whose reads were already local.</summary>
    internal static CachePolicy PolicyFor(PropertyOutcome providerDescription, uint targetPid)
    {
        var text = providerDescription.Text();
        if (!text.IsKnown() || text.Value is not { } description)
        {
            return CachePolicy.Batch;
        }

        return ServedOnlyByClientSideProviders(description, targetPid) ? CachePolicy.Skip : CachePolicy.Batch;
    }

    /// <summary>Builds the request the walk uses and returns one owned reference to it.
    ///
    /// The element mode is not set here: Client.CreateCacheRequest yields a request already in full
    /// element mode, and a null mode would leave a client with no access to uncached properties or
    /// control patterns, breaking every action that follows the walk.
    ///
    /// The tree scope is element scope, because the scope is relative to the element the call
    /// returns and omitting it silently fails to cache that element's own properties. The walk
    /// retrieves each node itself, so it needs no wider scope.
    ///
    /// Only the properties the walk reads are requested, IsPassword among them, so the secure-field
    /// gate has its input in the same batch as the properties it gates. A failing step releases the
    /// request and raises the UI Automation error for that step.</summary>
    internal static IntPtr BuildWalkCacheRequest(Client client)
    {
        var hr = client.CreateCacheRequest(out var request);
        if (hr < 0 || request == IntPtr.Zero)
        {
            Calls.Release(request);
            throw UiaFailure.Error(hr, "create a cache request");
        }

        try
        {
            Check(Calls.CallWithInt(request, Slots.CacheRequestPutTreeScope, Slots.TreeScopeElement), "set the cache tree scope");
            foreach (var property in PropertyIds.WalkSet)
            {
                Check(Calls.CallWithInt(request, Slots.CacheRequestAddProperty, PropertyUia.ToUiaProperty(property)), "add a property to the cache request");
            }
        }
        catch (AdapterError)
        {
            Calls.Release(request);
            throw;
        }

        return request;
    }

    /// <summary>Reads the root's ProviderDescription and its own process id, and picks the policy.
    ///
    /// The description never leaves this method: it names modules, process ids and provider ids, and
    /// trace segments clone adapter error text, so it must not reach an error or a log line. A root
    /// whose process id cannot be read falls back to Batch, because nothing here can rule out a
    /// provider delegated into that process.</summary>
    internal static CachePolicy PolicyForRoot(Element root)
    {
        if (root.CurrentProcessId() is not int pid)
        {
            return CachePolicy.Batch;
        }

        return PolicyFor(PropertyReader.ReadOne(root, TreeProperty.ProviderDescription), (uint)pid);
    }

    /// <summary>Reports whether every module named in a provider description is the client-side proxy
    /// library, and no provider layer is delegated into the target's own process.
    ///
    /// The module-name check alone is not sufficient: a WinForms or MSAA-bridged control commonly reports
    /// a description whose every named module is uiautomationcore.dll, with the "Main" layer's pid naming
    /// the target application's own process. The library is then loaded and running in that process, not
    /// in this client, so every read still crosses the same process boundary a foreign module name would
    /// signal. A24-17 measured a 176-node WinForms broad-search walk at about 840 ms live against about
    /// 310 ms forced-batched on exactly this shape, which is why the target's own pid is checked as well
    /// as the module names.</summary>
    private static bool ServedOnlyByClientSideProviders(string description, uint targetPid)
    {
        if (NamesTheProcess(description, targetPid))
        {
            return false;
        }

        var namedModule = false;
        foreach (var token in ModuleNameTokens(AsciiLower(description)))
        {
            if (!token.EndsWith(".dll", StringComparison.Ordinal) && !token.EndsWith(".exe", StringComparison.Ordinal))
            {
                continue;
            }

            namedModule = true;
            if (token != ClientSideProviderModule)
            {
                return false;
            }
        }

        return namedModule;
    }

    /// <summary>Whether a pid: marker anywhere in the description names the target process exactly, not
    /// merely as a numeric prefix: pid:6252 must not match a description naming pid:62520.</summary>
    private static bool NamesTheProcess(string description, uint targetPid)
    {
        var rest = AsciiLower(description);
        while (true)
        {
            var index = rest.IndexOf(PidMarker, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var after = rest[(index + PidMarker.Length)..];
            if (uint.TryParse(LeadingAsciiDigits(after), out var named) && named == targetPid)
            {
                return true;
            }

            rest = after;
        }
    }

    private static string LeadingAsciiDigits(string fragment)
    {
        var end = 0;
        while (end < fragment.Length && char.IsAsciiDigit(fragment[end]))
        {
            end += 1;
        }

        return fragment[..end];
    }

    /// <summary>Splits a lowered description on every character that is not an ASCII letter, digit, dot,
    /// underscore or hyphen. Empty runs are yielded too and never match a module suffix.</summary>
    private static IEnumerable<string> ModuleNameTokens(string lowered)
    {
        var start = 0;
        for (var index = 0; index <= lowered.Length; index += 1)
        {
            if (index == lowered.Length || !IsNameCharacter(lowered[index]))
            {
                yield return lowered[start..index];
                start = index + 1;
            }
        }
    }

    private static bool IsNameCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';

    /// <summary>Lowercases ASCII letters only. Non-ASCII characters are left as they are, so a
    /// Unicode letter never folds into an ASCII module name or pid marker.</summary>
    private static string AsciiLower(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index += 1)
            {
                var character = source[index];
                span[index] = character is >= 'A' and <= 'Z' ? (char)(character + 32) : character;
            }
        });

    private static void Check(int hr, string context)
    {
        if (hr < 0)
        {
            throw UiaFailure.Error(hr, context);
        }
    }}
