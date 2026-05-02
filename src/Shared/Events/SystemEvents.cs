using ComCross.Shared.Models;

namespace ComCross.Shared.Events;

/// <summary>
/// Base class for all system events
/// </summary>
public abstract record SystemEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Session create request (e.g. from plugin UI "Connect" action)
/// </summary>
public sealed record SessionCreateRequestEvent(
    string PluginId,
    string CapabilityId,
    string? SessionId,
    IDictionary<string, object>? Parameters
) : SystemEvent;

/// <summary>
/// Session was successfully created/registered in the core
/// </summary>
public sealed record SessionCreatedEvent(
    Session Session
) : SystemEvent;

/// <summary>
/// Session metadata or operational state changed after initial creation.
/// </summary>
public sealed record SessionUpdatedEvent(
    Session Session
) : SystemEvent;

/// <summary>
/// Connection status changed event
/// </summary>
public sealed record ConnectionStatusChangedEvent(
    string SessionId,
    SessionStatus OldStatus,
    SessionStatus NewStatus
) : SystemEvent;

/// <summary>
/// Device connected event
/// </summary>
public sealed record DeviceConnectedEvent(
    string SessionId,
    string Port
) : SystemEvent;

/// <summary>
/// Device disconnected event
/// </summary>
public sealed record DeviceDisconnectedEvent(
    string SessionId,
    string Port,
    string? Reason
) : SystemEvent;
/// <summary>
/// Session was closed/terminated
/// </summary>
public sealed record SessionClosedEvent(
    string SessionId,
    string? Reason = null
) : SystemEvent;

/// <summary>
/// PluginHost reported that a session ended at the transport/plugin layer.
/// </summary>
public sealed record PluginHostSessionClosedCoreEvent(
    string PluginId,
    string SessionId,
    string? Reason = null,
    bool RemoteInitiated = false,
    string? Error = null
) : SystemEvent;

/// <summary>
/// Session was removed from workspace state and in-memory session navigation.
/// </summary>
public sealed record SessionDeletedEvent(
    string SessionId
) : SystemEvent;

/// <summary>
/// Session display name was changed.
/// </summary>
public sealed record SessionRenamedEvent(
    string SessionId,
    string Name
) : SystemEvent;
/// <summary>
/// Data received event
/// </summary>
public sealed record DataReceivedEvent(
    string SessionId,
    byte[] Data,
    int BytesRead
) : SystemEvent;

/// <summary>
/// Data sent event
/// </summary>
public sealed record DataSentEvent(
    string SessionId,
    byte[] Data,
    int BytesSent
) : SystemEvent;

/// <summary>
/// Tool activated event
/// </summary>
public sealed record ToolActivatedEvent(
    string ToolId
) : SystemEvent;

/// <summary>
/// Tool deactivated event
/// </summary>
public sealed record ToolDeactivatedEvent(
    string ToolId
) : SystemEvent;

/// <summary>
/// Plugin-reported UI state invalidation that has been routed into the core event bus.
/// </summary>
public sealed record PluginUiStateInvalidatedCoreEvent(
    string PluginId,
    string CapabilityId,
    string? SessionId = null,
    string? ViewKind = null,
    string? ViewInstanceId = null,
    string? Reason = null,
    string? ResourceKind = null,
    string? ResourceId = null
) : SystemEvent;
