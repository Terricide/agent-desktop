namespace AgentDesktop.Core.Systems;

/// <summary>A DevTools endpoint returned only after the caller verifies listener ownership. Port comes from launch
/// setup; the remaining fields come from a live /json/version response constrained to that loopback port.</summary>
public sealed record CdpEndpoint
{
    public ushort Port { get; init; }

    public string HttpEndpoint { get; init; } = "";

    public string WebsocketUrl { get; init; } = "";

    public string? Product { get; init; }
}
