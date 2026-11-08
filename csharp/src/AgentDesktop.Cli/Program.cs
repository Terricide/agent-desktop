using AgentDesktop.Cli;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Windows;

var outcome = ArgParser.Parse(args);

if (outcome.Command is null)
{
    var attempted = args.Length > 0 ? args[0] : "agent-desktop";
    Console.Write(Envelope.Write(Envelope.Err(attempted, AdapterError.InvalidArgs(outcome.Error!))));
    Console.WriteLine();
    return ExitCodes.ParseError;
}

var root = StateRoot.FromEnvironment(() => Environment.GetEnvironmentVariable(StateRoot.EnvironmentVariable));
var adapter = new WindowsAdapter();
var response = Dispatch.Run(
    outcome.Command,
    adapter,
    new SnapshotStore(root),
    new SessionStore(root),
    Environment.GetEnvironmentVariable("AGENT_DESKTOP_SESSION"));

Console.Write(Envelope.Write(response));
Console.WriteLine();
return Envelope.ExitCode(response);
