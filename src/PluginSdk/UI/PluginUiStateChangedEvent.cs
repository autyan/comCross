namespace ComCross.PluginSdk.UI;

public sealed record PluginUiStateChangedEvent(
    string PluginId,
    string CapabilityId,
    string? SessionId,
    string? ViewKind,
    string? ViewInstanceId,
    string? ResourceKind,
    string? ResourceId,
    string Key,
    object? Value);
