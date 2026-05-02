namespace ComCross.Shared.Models;

public sealed record PluginHostSendDataPayload(
    string SessionId,
    byte[] Data,
    string? TransmitTargetId = null);
