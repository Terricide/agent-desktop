namespace AgentDesktop.Core.Response;

/// <summary>Where a command failure came from: an adapter refusal, io, serialization, or the tool itself.</summary>
public enum AppErrorKind
{
    Adapter,
    Io,
    Json,
    Internal,
}

/// <summary>A command failure. The CLI turns it into the envelope and the exit code.</summary>
public class AppError : Exception
{
    public AppErrorKind Kind { get; }

    public AdapterError? Adapter { get; }

    public bool IsPermissionDenied { get; }

    public AppError(AdapterError adapter) : base(adapter.Message)
    {
        Kind = AppErrorKind.Adapter;
        Adapter = adapter;
    }

    public AppError(string message) : base(message)
    {
        Kind = AppErrorKind.Internal;
    }

    /// <summary>A permission denial keeps its own code; guessing a code from an errno is how an unrelated failure acquires a confident, wrong label.</summary>
    public string Code => Kind switch
    {
        AppErrorKind.Adapter => Adapter!.Code.AsString(),
        AppErrorKind.Io when IsPermissionDenied => ErrorCode.PermDenied.AsString(),
        _ => "INTERNAL",
    };

    public string? Suggestion => Kind == AppErrorKind.Adapter ? Adapter!.Suggestion : null;

    public DeliverySemantics Disposition =>
        Kind == AppErrorKind.Adapter ? Adapter!.Disposition : DeliverySemantics.Unknown();

    public static AppError Stale(string refId) => new(AdapterError.Stale(refId));

    public static AppError InvalidInput(string message) =>
        new(AdapterError.InvalidArgs(message).WithDisposition(DeliverySemantics.NotDelivered()));

    public static AppError InvalidInput(string message, string suggestion) =>
        new(AdapterError.InvalidArgs(message)
            .WithSuggestion(suggestion)
            .WithDisposition(DeliverySemantics.NotDelivered()));
}
