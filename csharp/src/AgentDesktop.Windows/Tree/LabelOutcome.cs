namespace AgentDesktop.Windows.Tree;

/// <summary>What reading an element's `LabeledBy` relation produced. The four outcomes are not
/// interchangeable, and collapsing any two of them would either lose a name or publish one that
/// should have been withheld. They carry a kind rather than standing as bare singletons, because
/// three of them share the same shape and record equality would collapse them.</summary>
internal sealed record LabelOutcome
{
    public LabelOutcomeKind Kind { get; init; }

    public string? Text { get; init; }

    public static LabelOutcome Unlabelled => new() { Kind = LabelOutcomeKind.Unlabelled };

    /// <summary>The label resolved and the target reports `IsPassword`. A15-2 measured this case
    /// as constructible: a non-secure control labelled by a secure one. The referring element's
    /// own gate never sees it, because the content came from a different element.</summary>
    public static LabelOutcome Withheld => new() { Kind = LabelOutcomeKind.Withheld };

    /// <summary>The relation or the target's name could not be read.</summary>
    public static LabelOutcome Failed => new() { Kind = LabelOutcomeKind.Failed };

    public static LabelOutcome Labelled(string text) =>
        new() { Kind = LabelOutcomeKind.Labelled, Text = text };
}

internal enum LabelOutcomeKind
{
    Unlabelled,
    Labelled,
    Withheld,
    Failed,
}
