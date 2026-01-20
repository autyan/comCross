namespace ComCross.PluginSdk.UI;

public sealed record PluginUiStateChangedEvent(
    string PluginId,
    string CapabilityId,
    string? SessionId,
    string? ViewId,
    string Key,
    object? Value);
