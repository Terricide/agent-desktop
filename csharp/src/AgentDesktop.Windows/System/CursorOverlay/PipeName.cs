using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The name the overlay's control pipe is reached by.
/// </summary>
/// <remarks>
/// Pure: a deterministic function of the state root, the session id and the protocol generation, so any
/// process that knows those three computes the same name without a lookup file, a registry or a pid.
/// <para>
/// The generation is not decoration. A renderer left running by an earlier build keeps serving a rebuilt
/// binary's controls if the name it answers on does not change when the protocol does, which happens on any
/// rebuild during a live session. Hashing the generation in makes a stale renderer unreachable rather than
/// merely out of date.
/// </para>
/// </remarks>
internal static class PipeName
{
    /// <summary>
    /// Every generation this control protocol has ever had, oldest first, with the one this build speaks last.
    /// </summary>
    /// <remarks>
    /// Changing the wire format or the acknowledgement contract means appending here, and an append moves the
    /// outgoing generation into the retired set in the same edit. A maintainer cannot bump the generation
    /// without handing the previous one to the retirement sweep, so a renderer still drawing under it cannot be
    /// forgotten. With only the live generation the retired set is empty and the sweep is a no-op, which is the
    /// correct state for a protocol that has never changed.
    /// </remarks>
    internal static readonly IReadOnlyList<string> ProtocolGenerations = ["w1"];

    /// <summary>The generation this build speaks. A child whose marker names a different one refuses to serve.</summary>
    internal static string ProtocolGeneration => ProtocolGenerations[^1];

    /// <summary>The environment marker that turns an ordinary invocation of this binary into the overlay child.</summary>
    internal const string ChildMarker = "AGENT_DESKTOP_CURSOR_OVERLAY_CHILD";

    /// <summary>
    /// The argv token the child carries beside the marker. The environment block of another process is not
    /// readable from outside, so anything that recognises this child by inspection matches on the command line.
    /// </summary>
    internal const string ChildArgvFlag = "--cursor-overlay-child";

    /// <summary>The domain the agent segment is hashed under, so an agent id and a session id that are the same string cannot produce the same segment.</summary>
    private const string AgentHashTag = "agent";

    /// <summary>The endpoint one renderer answers on.</summary>
    /// <remarks>
    /// A session that names no agent keeps exactly the name it had before agents existed, so an overlay started
    /// by such a caller is still reached by the string it always was. An agent adds a second segment rather
    /// than being folded into the first, which keeps agent <c>a</c> of one session distinct from agent
    /// <c>a</c> of another: the first segment already carries the state root and the generation, and the second
    /// is hashed under its own tag, so no pair of ids can collapse into one value.
    /// </remarks>
    internal static string Build(string root, string sessionId, string? agentId) =>
        ForGeneration(root, sessionId, agentId, ProtocolGeneration);

    /// <summary>The endpoint one renderer answers on, named for the renderer that claims it.</summary>
    internal static string Name(string root, string sessionId, string? agentId) =>
        Build(root, sessionId, agentId);

    /// <summary>The endpoint a renderer of a given generation answers on.</summary>
    internal static string ForGeneration(string root, string sessionId, string? agentId, string generation)
    {
        var session = EndpointHash(root, sessionId, generation);
        if (agentId is not null)
        {
            return $@"\\.\pipe\agent-desktop-cursor-{session:x16}-{EndpointHash(root, agentId, AgentHashTag):x16}";
        }

        return $@"\\.\pipe\agent-desktop-cursor-{session:x16}";
    }

    /// <summary>
    /// What every one of a session's agent endpoints begins with, without the pipe prefix that enumeration
    /// does not report.
    /// </summary>
    /// <remarks>
    /// Discovery matches on this rather than on a list of agent ids, because those ids belong to whoever
    /// started the renderers and nothing here is told them. A <c>Disable</c> names no agent by design, so
    /// stopping a session means stopping whatever answers under its prefix.
    /// </remarks>
    internal static string BuildSessionPrefix(string root, string sessionId) =>
        $@"agent-desktop-cursor-{EndpointHash(root, sessionId, ProtocolGeneration):x16}-";

    /// <summary>
    /// The generations a renderer may still be drawing under that this build can no longer talk to: every
    /// entry of the ledger but the last.
    /// </summary>
    /// <remarks>
    /// Takes the ledger rather than reading the shipped one, so the promotion rule, that appending retires the
    /// entry it displaces, is provable against a ledger with more than one generation in it, which the shipped
    /// one need not have.
    /// </remarks>
    internal static IReadOnlyList<string> RetiredGenerations(IReadOnlyList<string> ledger) =>
        ledger.Count == 0 ? [] : ledger.Take(ledger.Count - 1).ToList();

    /// <summary>
    /// The argv the child is spawned with: the flag, its session, and its generation. Carried as arguments
    /// rather than only in the environment so a process that cannot read the child's environment can still
    /// recognise it.
    /// </summary>
    internal static IReadOnlyList<string> BuildChildArguments(string sessionId, string? agentId)
    {
        var arguments = new List<string> { ChildArgvFlag, sessionId, ProtocolGeneration };
        if (agentId is not null)
        {
            arguments.Add(agentId);
        }

        return arguments;
    }

    /// <summary>
    /// The session and generation a command line names, when it is one of ours. The agent token is optional
    /// and trails the generation, so a command line written before agents existed still parses as the
    /// agent-less child it is. An empty token is read as absent rather than as an agent named nothing, which is
    /// the shape a shell that expanded an unset variable produces.
    /// </summary>
    internal static (string SessionId, string Generation, string? AgentId)? ParseChildArguments(string[] arguments)
    {
        var flag = Array.IndexOf(arguments, ChildArgvFlag);
        if (flag < 0)
        {
            return null;
        }

        if (flag + 2 >= arguments.Length)
        {
            return null;
        }

        var sessionId = arguments[flag + 1];
        var generation = arguments[flag + 2];
        if (sessionId.Length == 0 || generation.Length == 0)
        {
            return null;
        }

        var agentId = flag + 3 < arguments.Length && arguments[flag + 3].Length > 0
            ? arguments[flag + 3]
            : null;
        return (sessionId, generation, agentId);
    }

    /// <summary>
    /// The endpoint segment: FNV-1a over the state root, the id and the tag, in that order. Concatenating the
    /// three before encoding is the same byte sequence the reference chains, so the same inputs give the same
    /// segment on both platforms.
    /// </summary>
    private static ulong EndpointHash(string root, string id, string tag)
    {
        var hash = 0xcbf29ce484222325UL;
        foreach (var byteValue in Encoding.UTF8.GetBytes(root + id + tag))
        {
            hash ^= byteValue;
            hash *= 0x1000001b3UL;
        }

        return hash;
    }
}
