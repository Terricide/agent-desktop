using System.Globalization;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Snapshot;

/// <summary>Writes one snapshot tree node with its keys in sorted order. The envelope
/// contract sorts the keys of every object it emits, and a field appears only when it
/// carries a value, so the tree is written field by field in that order rather than in
/// the order the node type declares its groups.</summary>
public static class TreeJson
{
    /// <summary>Writes the node and its subtree inside the object the caller opened.</summary>
    public static void Write(EnvelopeWriter w, AccessibilityNode node)
    {
        w.ObjectStart();
        StringList(w, "available_actions", node.Presentation.AvailableActions);
        Bounds(w, node.Presentation.Bounds);
        Children(w, node.Children);
        if (node.ChildrenCount > 0)
        {
            w.Key("children_count");
            w.Number((long)node.ChildrenCount.Value);
        }

        Text(w, "description", node.Identity.Description);
        StringList(w, "dom_classes", node.Presentation.Descriptors.DomClasses);
        Text(w, "hint", node.Presentation.Hint);
        Text(w, "name", node.Identity.Name);
        NativeId(w, node.Identity.NativeId);
        Text(w, "placeholder", node.Presentation.Descriptors.Placeholder);
        Text(w, "ref_id", node.RefId);
        w.Key("role");
        w.String(node.Role);
        Text(w, "role_description", node.Presentation.Descriptors.RoleDescription);
        StringList(w, "states", node.Presentation.States);
        Text(w, "subrole", node.Presentation.Descriptors.Subrole);
        if (node.SubtreeTruncated)
        {
            w.Key("subtree_truncated");
            w.Bool(true);
        }

        Text(w, "value", node.Identity.Value);
        w.ObjectEnd();
    }

    private static void Children(EnvelopeWriter w, List<AccessibilityNode> children)
    {
        if (children.Count == 0)
        {
            return;
        }

        w.Key("children");
        w.ArrayStart();
        foreach (var child in children)
        {
            w.Member();
            Write(w, child);
        }

        w.ArrayEnd();
    }

    private static void StringList(EnvelopeWriter w, string name, List<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        w.Key(name);
        w.ArrayStart();
        foreach (var value in values)
        {
            w.Member();
            w.String(value);
        }

        w.ArrayEnd();
    }

    private static void Bounds(EnvelopeWriter w, Rect? bounds)
    {
        if (bounds is null)
        {
            return;
        }

        var rect = bounds.Value;
        w.Key("bounds");
        w.ObjectStart();
        Geometry(w, "height", rect.Height);
        Geometry(w, "width", rect.Width);
        Geometry(w, "x", rect.X);
        Geometry(w, "y", rect.Y);
        w.ObjectEnd();
    }

    private static void NativeId(EnvelopeWriter w, ElementIdentifier? identifier)
    {
        if (identifier is null)
        {
            return;
        }

        w.Key("native_id");
        w.ObjectStart();
        w.Key("kind");
        w.String(KindName(identifier.Kind));
        w.Key("value");
        w.String(identifier.Value);
        w.ObjectEnd();
    }

    private static string KindName(IdentifierKind kind) => kind switch
    {
        IdentifierKind.AutomationId => "automation_id",
        IdentifierKind.NativeId => "native_id",
        IdentifierKind.AccessibilityId => "accessibility_id",
        IdentifierKind.RuntimeId => "runtime_id",
        _ => "unknown",
    };

    private static void Text(EnvelopeWriter w, string name, string? value)
    {
        if (value is null)
        {
            return;
        }

        w.Key(name);
        w.String(value);
    }

    /// <summary>A geometry value as the envelope writes it: a decimal point is always
    /// present, so a whole coordinate reads as 34.0, not 34.</summary>
    private static void Geometry(EnvelopeWriter w, string name, double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('e') && !text.Contains('E'))
        {
            text += ".0";
        }

        w.Key(name);
        w.Raw(text);
    }
}
