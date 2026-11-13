using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

public sealed class PrivateFileNameTests
{
    private const string Destination = "refmap.json";

    private static string NonceOf(string temporary)
    {
        var prefix = $".{Destination}.";
        Assert.StartsWith(prefix, temporary);
        Assert.EndsWith(".tmp", temporary);
        return temporary[prefix.Length..^4];
    }

    [Fact]
    public void TemporaryFileNameHasTheHiddenHexNonceShapeAndVariesBetweenCalls()
    {
        var first = PrivateFileName.TemporaryFileName(Destination);
        var nonce = NonceOf(first);

        Assert.Equal(16, nonce.Length);
        Assert.All(nonce, digit => Assert.True(char.IsAsciiHexDigitLower(digit), "the nonce must be hexadecimal"));

        var second = PrivateFileName.TemporaryFileName(Destination);
        Assert.NotEqual(first, second);
        Assert.NotEqual(NonceOf(second), nonce);
    }

    [Fact]
    public void TemporaryFileNameLeadsWithADotAndKeepsTheDestinationLeaf()
    {
        var temporary = PrivateFileName.TemporaryFileName("latest.json");

        Assert.StartsWith(".latest.json.", temporary);
        Assert.EndsWith(".tmp", temporary);
    }

    [Fact]
    public void TemporaryFileNameIsNotTheDestinationItNames()
    {
        var temporary = PrivateFileName.TemporaryFileName(Destination);

        Assert.NotEqual(Destination, temporary);
        Assert.False(temporary.Contains('\\'));
        Assert.False(temporary.Contains('/'));
    }
}
