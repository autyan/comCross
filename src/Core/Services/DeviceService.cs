using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Core.Services;

/// <summary>
/// Manages device connections and sessions
/// </summary>
public sealed class DeviceService : IDisposable
{
    private readonly PluginManagerService _pluginManager;
    private readonly IEventBus _eventBus;
    private readonly IMessageStreamService _messageStream;
    private readonly NotificationService _notificationService;
    private readonly SharedMemorySessionService _shmSessionService;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    // Non-persistent default naming counters: adapterKey -> nextIndex
    private readonly ConcurrentDictionary<string, int> _defaultSessionNameCounters = new(StringComparer.Ordinal);

    public DeviceService(
        PluginManagerService pluginManager,
        IEventBus eventBus,
        IMessageStreamService messageStream,
        NotificationService notificationService,
        SharedMemorySessionService shmSessionService)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _shmSessionService = shmSessionService ?? throw new ArgumentNullException(nameof(shmSessionService));
        
        // Subscribe to data events for all sessions (to update stats)
        _eventBus.Subscribe<DataReceivedEvent>(e => {
            if (_sessions.TryGetValue(e.SessionId, out var state)) {
                state.Session.RxBytes += e.BytesRead;
            }
        });
        
        _eventBus.Subscribe<DataSentEvent>(e => {
            if (_sessions.TryGetValue(e.SessionId, out var state)) {
                state.Session.TxBytes += e.BytesSent;
            }
        });
    }

    public async Task<Session> ConnectAsync(
        string pluginId,
        string capabilityId,
        string sessionId,
        string? name,
        JsonElement parameters,
        CancellationToken cancellationToken = default)
    {
        var runtime = _pluginManager.GetRuntime(pluginId) 
            ?? throw new InvalidOperationException($"Plugin {pluginId} not found");

        var finalName = !string.IsNullOrWhiteSpace(name)
            ? name!
            : GenerateDefaultSessionName(runtime, pluginId, capabilityId);

        var port = TryGetString(parameters, "port") ?? TryGetString(parameters, "Port") ?? $"{pluginId}:{capabilityId}";
        var baudRate = TryGetInt(parameters, "baudRate") ?? TryGetInt(parameters, "BaudRate") ?? 0;

        var session = new Session
        {
            Id = sessionId,
            Name = finalName,
            Port = port,
            BaudRate = baudRate,
            AdapterId = $"plugin:{pluginId}:{capabilityId}",
            Status = SessionStatus.Connecting,
            PluginId = pluginId,
            CapabilityId = capabilityId,
            ParametersJson = parameters.GetRawText()
        };

        // 1. Allocate Shared Memory
        // TODO: Get requested size from capability descriptor
        var descriptor = await _shmSessionService.AllocateOrReplaceAsync(sessionId, 256 * 1024);

        // 2. Apply segment to Plugin
        await runtime.Client!.SendAsync(
            new PluginHostRequest(
                Guid.NewGuid().ToString("N"),
                PluginHostMessageTypes.ApplySharedMemorySegment,
                SessionId: sessionId,
                Payload: JsonSerializer.SerializeToElement(new PluginHostApplySharedMemorySegmentPayload(sessionId, descriptor))),
            TimeSpan.FromSeconds(2));

        // 3. Connect
        var connectPayload = new PluginHostConnectPayload(capabilityId, parameters, sessionId);
        var response = await runtime.Client!.SendAsync(
            new PluginHostRequest(
                Guid.NewGuid().ToString("N"),
                PluginHostMessageTypes.Connect,
                SessionId: sessionId,
                Payload: JsonSerializer.SerializeToElement(connectPayload)),
            TimeSpan.FromSeconds(10));

        if (response is not { Ok: true })
        {
            await _shmSessionService.ReleaseSegmentAsync(sessionId);
            throw new InvalidOperationException(response?.Error ?? "Plugin connection failed");
        }

        session.Status = SessionStatus.Connected;
        session.StartTime = DateTime.UtcNow;

        _sessions[sessionId] = new SessionState(session, pluginId);
        
        // Start reading loop in background if not already started by service
        _shmSessionService.StartReading(sessionId);

        _eventBus.Publish(new SessionCreatedEvent(session));
        return session;
    }

    public async Task DisconnectAsync(string sessionId, string? reason = null)
    {
        if (_sessions.TryRemove(sessionId, out var state))
        {
            var runtime = _pluginManager.GetRuntime(state.PluginId);
            if (runtime?.Client != null)
            {
                var payload = new PluginHostDisconnectPayload(sessionId, reason);
                await runtime.Client.SendAsync(
                    new PluginHostRequest(
                        Guid.NewGuid().ToString("N"),
                        PluginHostMessageTypes.Disconnect,
                        SessionId: sessionId,
                        Payload: JsonSerializer.SerializeToElement(payload)),
                    TimeSpan.FromSeconds(5));
            }

            await _shmSessionService.ReleaseSegmentAsync(sessionId);
            _eventBus.Publish(new SessionClosedEvent(sessionId, reason));
        }
    }

    public async Task<int> SendAsync(string sessionId, byte[] data, MessageFormat format = MessageFormat.Text, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        // We could send data via plugin command (Standard out-of-band), 
        // but typically plugins read from their own input (e.g. Serial port reads).
        // For outgoing data to the plugin, we typically use a command.
        
        // TODO: Implement Plugin Send Command if needed, or plugins can manage it.
        // For now, let's assume the plugin handles its own Tx logic.
        
        return 0;
    }

    public Session? GetSession(string sessionId) => _sessions.TryGetValue(sessionId, out var state) ? state.Session : null;

    public IReadOnlyList<Session> GetAllSessions() => _sessions.Values.Select(s => s.Session).ToList();

    public void Dispose()
    {
        foreach (var sessionId in _sessions.Keys)
        {
            DisconnectAsync(sessionId).GetAwaiter().GetResult();
        }
        _sessions.Clear();
    }

    private sealed record SessionState(Session Session, string PluginId);

    private string GenerateDefaultSessionName(PluginRuntime runtime, string pluginId, string capabilityId)
    {
        var adapterKey = $"plugin:{pluginId}:{capabilityId}";

        // Prefer capabilityId as the adapter name (e.g., "serial", "tcp").
        // Fallback to plugin manifest name/id.
        var display = !string.IsNullOrWhiteSpace(capabilityId)
            ? capabilityId
            : (!string.IsNullOrWhiteSpace(runtime.Info.Manifest.Name) ? runtime.Info.Manifest.Name : pluginId);

        var index = _defaultSessionNameCounters.AddOrUpdate(adapterKey, 1, (_, current) => checked(current + 1));
        return $"{display} #{index}";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
        {
            return value;
        }

        return null;
    }
}

