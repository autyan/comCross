namespace ComCross.Shared.Models;

/// <summary>
/// Session lifecycle event emitted by PluginHost after a plugin reports that a session ended.
/// </summary>
public sealed record PluginHostSessionClosedEvent(
    string SessionId,
    string? Reason = null,
    bool RemoteInitiated = false,
    string? Error = null);
