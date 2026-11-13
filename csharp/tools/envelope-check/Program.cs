using System.Text.Json.Nodes;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

var success = Envelope.Ok("snapshot", JsonNode.Parse("""{"ref_count": 14, "complete": true}"""));
Console.WriteLine(success.ToJsonString());

Console.WriteLine(Envelope.Err("click", AdapterError.Stale("e1")).ToJsonString());

var node = new AccessibilityNode
{
    RefId = "e1",
    Role = "button",
    Identity = new NodeIdentity { Name = LocatorField<string>.Known("Run") },
    Presentation = new NodePresentation
    {
        Hint = null,
        States = ["enabled"],
        AvailableActions = ["Press", "Show"],
        Bounds = new Rect(1, 2, 3, 4),
        RefBounds = new Rect(1, 2, 3, 4),
    },
    ChildrenCount = 0,
    Children = [],
};

var writer = new EnvelopeWriter();
node.WriteJson(writer);
Console.WriteLine(writer.Text());
