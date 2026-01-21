namespace ComCross.Shared.Models;

/// <summary>
/// Indicates the plugin's UI state snapshot should be re-fetched by the main process.
/// </summary>
public sealed record PluginHostUiStateInvalidatedEvent(
    string CapabilityId,
    string? SessionId = null,
    string? ViewKind = null,
    string? ViewInstanceId = null,
    string? Reason = null);
