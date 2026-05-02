namespace ComCross.Shared.Models;

/// <summary>
/// Payload for requesting a plugin-provided UI state snapshot.
///
/// This is intentionally generic: the plugin may return arbitrary JSON state
/// which the main process can use to render bus-specific panels.
/// </summary>
using System.Text.Json;

public sealed record PluginHostGetUiStatePayload(
    string CapabilityId,
    string? SessionId = null,
    string? ViewKind = null,
    string? ViewInstanceId = null,
    string? PluginId = null,
    string? ResourceKind = null,
    string? ResourceId = null,
    IReadOnlyDictionary<string, JsonElement>? Settings = null);
