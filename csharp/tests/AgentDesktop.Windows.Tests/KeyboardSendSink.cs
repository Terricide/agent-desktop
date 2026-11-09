using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>The test collection that owns the shared `SendInput` and key-state seams. Any class
/// that replaces `KeyboardSend.PostSink` must declare this collection: the seam is one global
/// static, so two classes substituting it in parallel would overwrite each other and record each
/// other's events. xUnit runs classes in one collection sequentially, and tests within a class
/// sequentially, so each class gets exclusive use of the seam for its run.</summary>
[CollectionDefinition(Name)]
public class KeyboardSendSinkCollection : ICollectionFixture<KeyboardSendSinkFixture>
{
    /// <summary>The name every seam-substituting test class declares.</summary>
    public const string Name = "keyboard-send-sink";
}

/// <summary>The fixture that lets classes join the shared seam collection. It carries no state.</summary>
public sealed class KeyboardSendSinkFixture
{
}
