using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Core.Services;

namespace ComCross.Core.Services;

/// <summary>
/// Manages device connections and sessions
/// </summary>
public sealed class DeviceService : IDisposable, IAsyncDisposable
{
    private readonly PluginManagerService _pluginManager;
    private readonly IEventBus _eventBus;
    private readonly IMessageStreamService _messageStream;
    private readonly IFrameStore _frameStore;
    private readonly NotificationService _notificationService;
    private readonly SharedMemorySessionService _shmSessionService;
    private readonly SessionHostRuntimeService _sessionHostRuntimeService;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private readonly object _sessionOrderGate = new();
    private readonly List<string> _sessionOrder = new();
    private Task? _disposeTask;
    private int _disposeStarted;

    // Non-persistent default naming counters: adapterKey -> nextIndex
    private readonly ConcurrentDictionary<string, int> _defaultSessionNameCounters = new(StringComparer.Ordinal);

    public DeviceService(
        PluginManagerService pluginManager,
        IEventBus eventBus,
        IMessageStreamService messageStream,
        IFrameStore frameStore,
        NotificationService notificationService,
        SharedMemorySessionService shmSessionService,
        SessionHostRuntimeService sessionHostRuntimeService)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _shmSessionService = shmSessionService ?? throw new ArgumentNullException(nameof(shmSessionService));
        _sessionHostRuntimeService = sessionHostRuntimeService ?? throw new ArgumentNullException(nameof(sessionHostRuntimeService));
        
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

        _eventBus.Subscribe<PluginHostSessionClosedCoreEvent>(e => _ = MarkSessionClosedFromHostAsync(e));
    }

    public async Task<Session> ConnectAsync(
        string pluginId,
        string capabilityId,
        string sessionId,
        string? name,
        JsonElement parameters,
        string? scopeSessionId = null,
        string? resourceKind = null,
        string? resourceId = null,
        CancellationToken cancellationToken = default)
    {
        var runtime = _pluginManager.GetRuntime(pluginId) 
            ?? throw new InvalidOperationException($"Plugin {pluginId} not found");

        var finalName = !string.IsNullOrWhiteSpace(name)
            ? name!
            : _sessions.TryGetValue(sessionId, out var existingNameState) && !string.IsNullOrWhiteSpace(existingNameState.Session.Name)
                ? existingNameState.Session.Name
                : GenerateDefaultSessionName(runtime, pluginId, capabilityId);

        if (_sessions.TryGetValue(sessionId, out var existingState)
            && (!string.Equals(existingState.Session.PluginId, pluginId, StringComparison.Ordinal)
                || !string.Equals(existingState.Session.CapabilityId, capabilityId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Cannot reconnect an existing session with a different plugin capability.");
        }

        // Sessions are host-level entries. ParametersJson is committed only on successful connect.
        var session = GetOrCreateSession(sessionId, finalName, pluginId, capabilityId);
        session.Status = SessionStatus.Connecting;

        // Listener topology hints (used by UI + orchestration).
        ApplyListenerTopologyHints(session, pluginId, capabilityId, parameters, scopeSessionId, resourceKind, resourceId);

        var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
        var sessionHostModel = ResolveSessionHostModel(capability);
        var supportsMultiSession = sessionHostModel is not SessionHostModel.DedicatedPerSession;

        var multiSessionGroupId = ComputeMultiSessionGroupId(sessionId, sessionHostModel, capability, parameters, scopeSessionId);

        // 1. Allocate Shared Memory (segment allocation is safe before connect; applying it requires an active session)
        var requestedBytes = capability?.SharedMemoryRequest?.PreferredBytes is > 0
            ? capability.SharedMemoryRequest.PreferredBytes
            : capability?.SharedMemoryRequest?.MinBytes is > 0
                ? capability.SharedMemoryRequest.MinBytes
                : 256 * 1024;

        var descriptor = await _shmSessionService.AllocateOrReplaceAsync(sessionId, requestedBytes);

        // 2. Start Session Host
        // Default: 1 session : 1 process.
        // Capability may declare a shared session-host model (SharedPerCapability / SharedPerScope).
        var sessionHost = await _sessionHostRuntimeService.EnsureStartedAsync(
            runtime.Info,
            sessionId,
            capabilityId,
            supportsMultiSession,
            multiSessionGroupId,
            cancellationToken);

        try
        {
            // 3. Connect via Session Host
            var connectPayload = new PluginHostConnectPayload(
                capabilityId,
                parameters,
                sessionId,
                scopeSessionId,
                resourceKind,
                resourceId);
            var response = await sessionHost.Client.SendAsync(
                new PluginHostRequest(
                    Guid.NewGuid().ToString("N"),
                    PluginHostMessageTypes.Connect,
                    SessionId: sessionId,
                    Payload: JsonSerializer.SerializeToElement(connectPayload)),
                TimeSpan.FromSeconds(10));

            if (response is not { Ok: true })
            {
                session.Status = SessionStatus.Disconnected;
                throw new InvalidOperationException(response?.Error ?? "Plugin connection failed");
            }

            if (response.Payload is { ValueKind: JsonValueKind.Object } resultPayload)
            {
                ApplyConnectResultMetadata(session, resultPayload);
            }

            // 4. Apply segment to Session Host (requires active session in plugin host)
            var applyResponse = await sessionHost.Client.SendAsync(
                new PluginHostRequest(
                    Guid.NewGuid().ToString("N"),
                    PluginHostMessageTypes.ApplySharedMemorySegment,
                    SessionId: sessionId,
                    Payload: JsonSerializer.SerializeToElement(new PluginHostApplySharedMemorySegmentPayload(sessionId, descriptor))),
                TimeSpan.FromSeconds(2));

            if (applyResponse is not { Ok: true })
            {
                session.Status = SessionStatus.Disconnected;
                throw new InvalidOperationException(applyResponse?.Error ?? "Apply shared memory segment failed");
            }
        }
        catch
        {
            // Hard cleanup on connect/apply failure.
            try
            {
                await sessionHost.Client.SendAsync(
                    new PluginHostRequest(
                        Guid.NewGuid().ToString("N"),
                        PluginHostMessageTypes.Disconnect,
                        SessionId: sessionId,
                        Payload: JsonSerializer.SerializeToElement(new PluginHostDisconnectPayload(sessionId, "connect-failed"))),
                    TimeSpan.FromSeconds(2));
            }
            catch
            {
                // best-effort
            }

            await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: "connect-failed");
            await _shmSessionService.ReleaseSegmentAsync(sessionId);
            throw;
        }

        session.Status = SessionStatus.Connected;
        session.StartTime = DateTime.UtcNow;

        // Commit last successful connection parameters. Plugins may replace this with enriched parameters.
        if (string.IsNullOrWhiteSpace(session.ParametersJson))
        {
            session.ParametersJson = parameters.GetRawText();
        }

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
            var oldStatus = state.Session.Status;
            if (oldStatus == SessionStatus.Connected || oldStatus == SessionStatus.Connecting)
            {
                state.Session.Status = SessionStatus.Closing;
                _eventBus.Publish(new ConnectionStatusChangedEvent(sessionId, oldStatus, SessionStatus.Closing));
            }

            var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
            if (sessionHost is not null)
            {
                try
                {
                    var payload = new PluginHostDisconnectPayload(sessionId, reason);
                    await sessionHost.Client.SendAsync(
                        new PluginHostRequest(
                            Guid.NewGuid().ToString("N"),
                            PluginHostMessageTypes.Disconnect,
                            SessionId: sessionId,
                            Payload: JsonSerializer.SerializeToElement(payload)),
                        TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // best-effort
                }

                await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: reason);
            }

            await _shmSessionService.ReleaseSegmentAsync(sessionId);
            oldStatus = state.Session.Status;
            state.Session.Status = SessionStatus.Disconnected;
            if (oldStatus != SessionStatus.Disconnected)
            {
                _eventBus.Publish(new ConnectionStatusChangedEvent(sessionId, oldStatus, SessionStatus.Disconnected));
            }

            _eventBus.Publish(new SessionClosedEvent(sessionId, reason));
        }
    }

    private async Task MarkSessionClosedFromHostAsync(PluginHostSessionClosedCoreEvent evt)
    {
        if (!_sessions.TryGetValue(evt.SessionId, out var state))
        {
            return;
        }

        var oldStatus = state.Session.Status;
        if (oldStatus == SessionStatus.Disconnected)
        {
            return;
        }

        state.Session.Status = SessionStatus.Disconnected;
        _eventBus.Publish(new ConnectionStatusChangedEvent(evt.SessionId, oldStatus, SessionStatus.Disconnected));

        try
        {
            await _shmSessionService.ReleaseSegmentAsync(evt.SessionId);
        }
        catch
        {
            // best-effort after transport-initiated close
        }

        try
        {
            await _sessionHostRuntimeService.StopAsync(evt.SessionId, TimeSpan.FromSeconds(1), reason: evt.Reason ?? "host-session-closed");
        }
        catch
        {
            // best-effort after transport-initiated close
        }

        var reason = evt.Reason;
        if (!string.IsNullOrWhiteSpace(evt.Error))
        {
            reason = string.IsNullOrWhiteSpace(reason) ? evt.Error : $"{reason}: {evt.Error}";
        }

        _eventBus.Publish(new SessionClosedEvent(evt.SessionId, reason));
    }

    public async Task<int> SendAsync(string sessionId, byte[] data, MessageFormat format = MessageFormat.Text, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out _))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        data ??= Array.Empty<byte>();

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            throw new InvalidOperationException($"Session host not running for session {sessionId}");
        }

        // Send via Session Host (real TX)
        var payload = JsonSerializer.SerializeToElement(new PluginHostSendDataPayload(sessionId, data));
        var response = await sessionHost.Client.SendAsync(
            new PluginHostRequest(
                Guid.NewGuid().ToString("N"),
                PluginHostMessageTypes.SendData,
                SessionId: sessionId,
                Payload: payload),
            TimeSpan.FromSeconds(3));

        if (response is null)
        {
            throw new InvalidOperationException("Session host unavailable.");
        }

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Send failed.");
        }

        // Mirror TX into the FrameStore so RX/TX share one timeline.
        _frameStore.Append(sessionId, DateTime.UtcNow, FrameDirection.Tx, data, format, source: "send-tx");
        _eventBus.Publish(new DataSentEvent(sessionId, data, data.Length));
        return data.Length;
    }

    public Session? GetSession(string sessionId) => _sessions.TryGetValue(sessionId, out var state) ? state.Session : null;

    public IReadOnlyList<Session> GetAllSessions()
    {
        lock (_sessionOrderGate)
        {
            var ordered = new List<Session>(_sessionOrder.Count);
            var orderedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sessionId in _sessionOrder)
            {
                if (_sessions.TryGetValue(sessionId, out var state))
                {
                    ordered.Add(state.Session);
                    orderedIds.Add(sessionId);
                }
            }

            foreach (var state in _sessions.Values)
            {
                if (orderedIds.Add(state.Session.Id))
                {
                    ordered.Add(state.Session);
                }
            }

            return ordered;
        }
    }

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
            DisplayTitle = descriptor.DisplayTitle,
            DisplaySubtitle = descriptor.DisplaySubtitle,
            EnableDatabaseStorage = descriptor.EnableDatabaseStorage,
            Kind = descriptor.Kind,
            ParentSessionId = descriptor.ParentSessionId,
            Status = SessionStatus.Disconnected
        };

        // Back-compat: older workspace-state.json may not persist Kind/ParentSessionId.
        // Re-infer listener topology from ParametersJson when available.
        if (!string.IsNullOrWhiteSpace(session.PluginId)
            && !string.IsNullOrWhiteSpace(session.CapabilityId)
            && !string.IsNullOrWhiteSpace(descriptor.ParametersJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(descriptor.ParametersJson);
                var parameters = doc.RootElement.Clone();
                ApplyListenerTopologyHints(session, session.PluginId!, session.CapabilityId!, parameters);
            }
            catch
            {
                // best-effort
            }
        }

        var added = false;
        var state = _sessions.GetOrAdd(descriptor.Id, _ =>
        {
            added = true;
            return new SessionState(session);
        });
        if (added)
        {
            TrackSessionOrder(descriptor.Id);
        }

        // If it already existed, keep instance but update fields.
        if (!ReferenceEquals(state.Session, session))
        {
            state.Session.Name = session.Name;
            state.Session.AdapterId = session.AdapterId;
            state.Session.PluginId = session.PluginId;
            state.Session.CapabilityId = session.CapabilityId;
            state.Session.ParametersJson = session.ParametersJson;
            state.Session.DisplayTitle = session.DisplayTitle;
            state.Session.DisplaySubtitle = session.DisplaySubtitle;
            state.Session.EnableDatabaseStorage = session.EnableDatabaseStorage;
            state.Session.Kind = session.Kind;
            state.Session.ParentSessionId = session.ParentSessionId;
            if (state.Session.Status != SessionStatus.Connected)
            {
                state.Session.Status = SessionStatus.Disconnected;
            }
        }

        _eventBus.Publish(new SessionCreatedEvent(state.Session));
    }

    public bool RemoveSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (!_sessions.TryRemove(sessionId, out _))
        {
            return false;
        }

        UntrackSessionOrder(sessionId);
        _frameStore.Clear(sessionId);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        foreach (var sessionId in _sessions.Keys)
        {
            _frameStore.Clear(sessionId);
        }

        _sessions.Clear();
        lock (_sessionOrderGate)
        {
            _sessionOrder.Clear();
        }
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
        {
            _disposeTask = DisposeAsyncCore();
        }

        return _disposeTask is null ? ValueTask.CompletedTask : new ValueTask(_disposeTask);
    }

    private async Task DisposeAsyncCore()
    {
        var sessionIds = _sessions.Keys.ToArray();

        foreach (var sessionId in sessionIds)
        {
            try
            {
                await DisconnectAsync(sessionId, "dispose");
            }
            catch
            {
                // best-effort during shutdown
            }

            _frameStore.Clear(sessionId);
        }

        _sessions.Clear();
        lock (_sessionOrderGate)
        {
            _sessionOrder.Clear();
        }
        GC.SuppressFinalize(this);
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
        TrackSessionOrder(sessionId);
        return session;
    }

    private void TrackSessionOrder(string sessionId)
    {
        lock (_sessionOrderGate)
        {
            if (!_sessionOrder.Contains(sessionId, StringComparer.Ordinal))
            {
                _sessionOrder.Add(sessionId);
            }
        }
    }

    private void UntrackSessionOrder(string sessionId)
    {
        lock (_sessionOrderGate)
        {
            _sessionOrder.RemoveAll(id => string.Equals(id, sessionId, StringComparison.Ordinal));
        }
    }

    private static void ApplyConnectResultMetadata(Session session, JsonElement payload)
    {
        try
        {
            var result = payload.Deserialize<PluginConnectResult>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is null)
            {
                return;
            }

            if (result.CommittedParameters is { ValueKind: JsonValueKind.Object } committed)
            {
                session.ParametersJson = committed.GetRawText();
            }

            session.DisplayTitle = result.DisplayTitle;
            session.DisplaySubtitle = result.DisplaySubtitle;

            if (!string.IsNullOrWhiteSpace(result.SessionKind))
            {
                session.Kind = string.Equals(result.SessionKind, "listener", StringComparison.OrdinalIgnoreCase)
                    ? SessionKind.Listener
                    : SessionKind.Connection;
            }

            if (!string.IsNullOrWhiteSpace(result.ParentSessionId))
            {
                session.ParentSessionId = result.ParentSessionId;
            }
        }
        catch
        {
            // Optional plugin metadata must not turn a successful connect into a failure.
        }
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

    private static SessionHostModel ResolveSessionHostModel(PluginCapabilityDescriptor? capability)
    {
        if (capability is null)
        {
            return SessionHostModel.DedicatedPerSession;
        }

        if (capability.SessionHostModel != SessionHostModel.Unspecified)
        {
            return capability.SessionHostModel;
        }

        // Backward compatibility: SupportsMultiSession implied shared-per-capability.
        return capability.SupportsMultiSession
            ? SessionHostModel.SharedPerCapability
            : SessionHostModel.DedicatedPerSession;
    }

    private static string? ComputeMultiSessionGroupId(
        string sessionId,
        SessionHostModel model,
        PluginCapabilityDescriptor? capability,
        JsonElement parameters,
        string? scopeSessionId = null)
    {
        if (model is SessionHostModel.DedicatedPerSession)
        {
            return null;
        }

        if (model is SessionHostModel.SharedPerCapability)
        {
            return null;
        }

        if (model is SessionHostModel.SharedPerScope)
        {
            var keyParam = capability?.SessionHostGroupKeyParameter;
            if (!string.IsNullOrWhiteSpace(keyParam))
            {
                var key = TryGetString(parameters, keyParam);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }
            }

            if (!string.IsNullOrWhiteSpace(scopeSessionId))
            {
                return scopeSessionId;
            }

            // Listener session itself may not provide a scope key; fall back to its own sessionId.
            return sessionId;
        }

        return null;
    }

    private static void ApplyListenerTopologyHints(
        Session session,
        string pluginId,
        string capabilityId,
        JsonElement parameters,
        string? scopeSessionId = null,
        string? resourceKind = null,
        string? resourceId = null)
    {
        if (!string.Equals(pluginId, "network.adapter", StringComparison.Ordinal))
        {
            return;
        }

        if (capabilityId is not ("tcp.server" or "udp.listen"))
        {
            return;
        }

        var mode = TryGetString(parameters, "mode");
        if (string.Equals(mode, "bind", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(scopeSessionId)
                && string.Equals(resourceKind, "pending", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(resourceId)))
        {
            session.Kind = SessionKind.Connection;
            session.ParentSessionId = !string.IsNullOrWhiteSpace(scopeSessionId)
                ? scopeSessionId
                : TryGetString(parameters, "listenerSessionId");
        }
        else
        {
            session.Kind = SessionKind.Listener;
            session.ParentSessionId = null;
        }
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
