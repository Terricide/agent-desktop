namespace AgentDesktop.Core.Adapter;

/// <summary>One platform's adapter: reading, acting, input, and system. A platform crate implements it; core never imports one.</summary>
public interface PlatformAdapter : ObservationOps, ActionOps, InputOps, SystemOps
{
}
