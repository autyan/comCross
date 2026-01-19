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
