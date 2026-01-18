namespace ComCross.Shared.Models;

/// <summary>
/// Per-session registration handshake emitted by PluginHost after a successful Connect.
/// Used by the main process to bind a sessionId to a specific host process and token.
/// </summary>
public sealed record PluginHostSessionRegisteredEvent(
    string Token,
    int ProcessId,
    string SessionId);
