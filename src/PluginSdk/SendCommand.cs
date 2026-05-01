namespace ComCross.PluginSdk;

public sealed record PluginSendCommand(
    string SessionId,
    byte[] Data,
    string? TransmitTargetId = null);

public sealed record PluginTransmitTarget(
    string Id,
    string DisplayName,
    string? Subtitle = null,
    bool IsDefault = false,
    DateTimeOffset? LastSeenUtc = null);

public sealed record PluginTransmitTargetQuery(
    string SessionId);

public sealed record PluginTransmitTargetSnapshot(
    IReadOnlyList<PluginTransmitTarget> Targets,
    string? DefaultTargetId = null,
    bool RequireTargetForSend = false,
    DateTimeOffset UpdatedAt = default);

public interface IPluginTransmitTargetProvider
{
    PluginTransmitTargetSnapshot GetTransmitTargets(PluginTransmitTargetQuery query);
}
