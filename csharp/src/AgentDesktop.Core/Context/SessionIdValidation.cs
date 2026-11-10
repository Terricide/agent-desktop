namespace AgentDesktop.Core.Context;

/// <summary>
/// Session ids are filesystem-safe names: 1 to 64 bytes of ASCII letters,
/// numbers, '-' or '_'. The same shape is used for agent ids.
/// </summary>
public static class SessionIdValidation
{
    private const int MaxLength = 64;

    /// <summary>Whether <paramref name="id"/> is a session id the state store can keep.</summary>
    public static bool IsValid(string id) => IsSafeName(id);

    internal static bool IsSafeName(string name)
    {
        if (name.Length == 0 || name.Length > MaxLength)
            return false;

        for (var index = 0; index < name.Length; index++)
        {
            if (!IsSafeByte(name[index]))
                return false;
        }

        return true;
    }

    private static bool IsSafeByte(char c) =>
        c is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9'
        or '-'
        or '_';
}
