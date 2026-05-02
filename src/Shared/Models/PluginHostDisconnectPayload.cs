namespace ComCross.Shared.Models;

public sealed record PluginHostDisconnectPayload(
    string SessionId,
    string? Reason = null);
