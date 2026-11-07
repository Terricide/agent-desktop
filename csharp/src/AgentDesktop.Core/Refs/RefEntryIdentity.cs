namespace AgentDesktop.Core.Refs;

/// <summary>The stable fields a ref's capture recorded. Control values and bounds are never
/// identity. A ref whose capture proof was not unique over one complete observation of its
/// window is refused before the application is read.</summary>
public sealed record RefEntryIdentity
{
    public string Role { get; init; } = "";
    public string? NativeId { get; init; }
    public string? StableName { get; init; }
    public string? Description { get; init; }
    public string WindowKey { get; init; } = "";
    public string? UniquenessReason { get; init; }

    public bool IsCaptureUnique => UniquenessReason is null;

    /// <summary>Role plus every recorded stable field. A capture that recorded nothing cannot
    /// match a capture that recorded something.</summary>
    public bool MatchesCapture(RefEntryIdentity other)
    {
        if (Role != other.Role)
        {
            return false;
        }
        if (NativeId != other.NativeId)
        {
            return false;
        }
        if (StableName is not null && StableName != other.StableName)
        {
            return false;
        }
        if (Description is not null && Description != other.Description)
        {
            return false;
        }
        return WindowKey == other.WindowKey;
    }
}
