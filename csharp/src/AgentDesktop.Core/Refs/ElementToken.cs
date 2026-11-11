namespace AgentDesktop.Core.Refs;

/// <summary>A ref-able element that keeps a provider element token anchors itself, and no
/// descendant needs a frame. A token is the provider's own handle, so identity does not have to
/// be counted for it. Port of <c>is_element_token</c> in
/// <c>crates/core/src/refs/ref_alloc.rs</c>.</summary>
internal static class ElementToken
{
    internal const string Prefix = "axtok1:";

    internal static bool IsElementToken(string? token) =>
        token is { } text && text.StartsWith(Prefix, StringComparison.Ordinal);
}
