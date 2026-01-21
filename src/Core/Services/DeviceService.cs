using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Core.Services;

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

        // Sessions are host-level entries. ParametersJson is committed only on successful connect.
        var session = GetOrCreateSession(sessionId, finalName, pluginId, capabilityId);
        session.Status = SessionStatus.Connecting;

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
            session.Status = SessionStatus.Disconnected;
            throw new InvalidOperationException(response?.Error ?? "Plugin connection failed");
        }

        session.Status = SessionStatus.Connected;
        session.StartTime = DateTime.UtcNow;

        // Commit last successful connection parameters.
        session.ParametersJson = parameters.GetRawText();

        _sessions[sessionId] = new SessionState(session);
        
        // Start reading loop in background if not already started by service
        _shmSessionService.StartReading(sessionId);

        // Publish as an upsert signal (new session or updated committed parameters).
        _eventBus.Publish(new SessionCreatedEvent(session));
        return session;
    }

    public async Task DisconnectAsync(string sessionId, string? reason = null)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            var runtime = !string.IsNullOrWhiteSpace(state.Session.PluginId)
                ? _pluginManager.GetRuntime(state.Session.PluginId)
                : null;

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
            state.Session.Status = SessionStatus.Disconnected;
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
        
        // v0.4.x: Plugins do not yet expose a standardized TX command.
        // Still append a TX log entry so the UI stays responsive and consistent.
        var content = format == MessageFormat.Hex
            ? $"TX: {BitConverter.ToString(data).Replace("-", " ")}" 
            : $"TX: {Encoding.UTF8.GetString(data)}";

        _messageStream.Append(sessionId, new LogMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Content = content,
            Level = LogLevel.Info,
            Source = "tx",
            RawData = data,
            Format = format
        });

        _eventBus.Publish(new DataSentEvent(sessionId, data, data.Length));
        return data.Length;
    }

    public Session? GetSession(string sessionId) => _sessions.TryGetValue(sessionId, out var state) ? state.Session : null;

    public IReadOnlyList<Session> GetAllSessions() => _sessions.Values.Select(s => s.Session).ToList();

    public void RestoreSession(SessionDescriptor descriptor)
    {
        if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.Id))
        {
            return;
        }

        var session = new Session
        {
            Id = descriptor.Id,
            Name = string.IsNullOrWhiteSpace(descriptor.Name) ? descriptor.Id : descriptor.Name,
            AdapterId = string.IsNullOrWhiteSpace(descriptor.AdapterId) ? "serial" : descriptor.AdapterId,
            PluginId = descriptor.PluginId,
            CapabilityId = descriptor.CapabilityId,
            ParametersJson = descriptor.ParametersJson,
            EnableDatabaseStorage = descriptor.EnableDatabaseStorage,
            Status = SessionStatus.Disconnected
        };

        var state = _sessions.GetOrAdd(descriptor.Id, _ => new SessionState(session));
        // If it already existed, keep instance but update fields.
        if (!ReferenceEquals(state.Session, session))
        {
            state.Session.Name = session.Name;
            state.Session.AdapterId = session.AdapterId;
            state.Session.PluginId = session.PluginId;
            state.Session.CapabilityId = session.CapabilityId;
            state.Session.ParametersJson = session.ParametersJson;
            state.Session.EnableDatabaseStorage = session.EnableDatabaseStorage;
            if (state.Session.Status != SessionStatus.Connected)
            {
                state.Session.Status = SessionStatus.Disconnected;
            }
        }

        _eventBus.Publish(new SessionCreatedEvent(state.Session));
    }

    public void Dispose()
    {
        foreach (var sessionId in _sessions.Keys)
        {
            DisconnectAsync(sessionId).GetAwaiter().GetResult();
        }
        _sessions.Clear();
    }

    private sealed record SessionState(Session Session);

    private Session GetOrCreateSession(string sessionId, string name, string pluginId, string capabilityId)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            // Session host entry already exists.
            existing.Session.Name = name;
            if (string.IsNullOrWhiteSpace(existing.Session.PluginId))
            {
                existing.Session.PluginId = pluginId;
            }
            if (string.IsNullOrWhiteSpace(existing.Session.CapabilityId))
            {
                existing.Session.CapabilityId = capabilityId;
            }
            if (string.IsNullOrWhiteSpace(existing.Session.AdapterId))
            {
                existing.Session.AdapterId = $"plugin:{pluginId}:{capabilityId}";
            }

            return existing.Session;
        }

        var session = new Session
        {
            Id = sessionId,
            Name = name,
            AdapterId = $"plugin:{pluginId}:{capabilityId}",
            Status = SessionStatus.Disconnected,
            PluginId = pluginId,
            CapabilityId = capabilityId
        };

        _sessions[sessionId] = new SessionState(session);
        return session;
    }

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

