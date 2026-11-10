using System.Security.Cryptography;

namespace AgentDesktop.Windows;

/// <summary>Names the temporaries a private write creates before they are promoted over their destination.
/// The name is the destination's leaf name wrapped as a hidden `.{name}.{nonce}.tmp` file, and the nonce is
/// fresh cryptographic randomness so a same-privilege racer cannot predict it. Callers loop over fresh
/// candidates when creation collides.</summary>
internal static class PrivateFileName
{
    private const int NonceBytes = 8;

    internal static string TemporaryFileName(string name)
    {
        Span<byte> bytes = stackalloc byte[NonceBytes];
        RandomNumberGenerator.Fill(bytes);
        return $".{name}.{BitConverter.ToUInt64(bytes):x16}.tmp";
    }
}
