using System.Text.Json;

namespace ComCross.PluginSdk;

/// <summary>
/// Query for plugin-maintained UI state.
///
/// - When SessionId is null: request "default" (no session selected) state.
/// - When SessionId is provided: request selected-session state.
/// - When ResourceKind/ResourceId are provided: request plugin-owned subresource state under the selected session.
/// - ViewKind scopes state for different UI surfaces (e.g. connect-dialog, sidebar-config).
/// - ViewInstanceId is an optional per-open instance identifier; plugins may ignore it.
/// </summary>
public sealed record PluginUiStateQuery(
    string CapabilityId,
    string? SessionId = null,
    string? ViewKind = null,
    string? ViewInstanceId = null,
    string? ResourceKind = null,
    string? ResourceId = null,
    IReadOnlyDictionary<string, JsonElement>? Settings = null);

/// <summary>
/// Snapshot returned by a plugin for UI rendering/state synchronization.
///
/// Note: implementers should return a JsonElement that is safe to serialize.
/// Prefer using JsonDocument.Parse(...).RootElement.Clone().
/// </summary>
public sealed record PluginUiStateSnapshot(
    JsonElement State,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Event raised by a plugin to indicate its UI state has changed.
/// The main process should re-fetch state via get-ui-state.
/// </summary>
public sealed record PluginUiStateInvalidatedEvent(
    string CapabilityId,
    string? SessionId = null,
    string? ViewKind = null,
    string? ViewInstanceId = null,
    string? Reason = null,
    string? ResourceKind = null,
    string? ResourceId = null);

/// <summary>
/// Optional event source for plugins that want to push UI-state changes.
/// </summary>
public interface IPluginUiStateEventSource
{
    event EventHandler<PluginUiStateInvalidatedEvent>? UiStateInvalidated;
}

/// <summary>
/// Event raised by a plugin when an active session has ended independently of a host disconnect request.
/// </summary>
public sealed record PluginSessionClosedEvent(
    string SessionId,
    string? Reason = null,
    bool RemoteInitiated = false,
    string? Error = null);

/// <summary>
/// Optional event source for plugins that can detect transport-level session closure.
/// </summary>
public interface IPluginSessionLifecycleEventSource
{
    event EventHandler<PluginSessionClosedEvent>? SessionClosed;
}

/// <summary>
/// Optional interface for plugins that want the main process to render
/// richer UI driven by plugin-maintained state (ports list, defaults, etc.).
/// </summary>
public interface IPluginUiStateProvider
{
    PluginUiStateSnapshot GetUiState(PluginUiStateQuery query);
}
