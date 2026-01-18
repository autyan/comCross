namespace ComCross.Shared.Models;

/// <summary>
/// Payload for requesting a plugin-provided UI state snapshot.
///
/// This is intentionally generic: the plugin may return arbitrary JSON state
/// which the main process can use to render bus-specific panels.
/// </summary>
public sealed record PluginHostGetUiStatePayload(
    string CapabilityId,
    string? SessionId = null,
    string? ViewId = null);
