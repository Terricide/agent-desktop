using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>One HRESULT's complete classification: its read disposition, its caller-facing error code, and an optional recovery hint.</summary>
internal sealed record HresultRecord(ReadDisposition Disposition, ErrorCode Code, string? Suggestion);
