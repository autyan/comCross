using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

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
    private readonly PluginHostProtocolService _pluginHostProtocol;
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
        SessionHostRuntimeService sessionHostRuntimeService,
        PluginHostProtocolService pluginHostProtocol)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _shmSessionService = shmSessionService ?? throw new ArgumentNullException(nameof(shmSessionService));
        _sessionHostRuntimeService = sessionHostRuntimeService ?? throw new ArgumentNullException(nameof(sessionHostRuntimeService));
        _pluginHostProtocol = pluginHostProtocol ?? throw new ArgumentNullException(nameof(pluginHostProtocol));
        
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

        if (_sessions.TryGetValue(sessionId, out var reconnectState)
            && !reconnectState.Session.CanReconnect
            && string.IsNullOrWhiteSpace(scopeSessionId)
            && string.IsNullOrWhiteSpace(resourceKind)
            && string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException("This session does not support reconnect.");
        }

        // Sessions are host-level entries. ParametersJson is committed only on successful connect.
        var session = GetOrCreateSession(sessionId, finalName, pluginId, capabilityId);
        session.Status = SessionStatus.Connecting;

        var result = await _pluginHostProtocol.ConnectSessionAsync(
            runtime,
            capabilityId,
            sessionId,
            parameters,
            scopeSessionId,
            resourceKind,
            resourceId,
            TimeSpan.FromSeconds(10),
            cancellationToken);

        if (!result.Ok)
        {
            session.Status = SessionStatus.Disconnected;
            throw new InvalidOperationException(result.Error ?? "Plugin connection failed");
        }

        ApplyConnectResultMetadata(session, result);
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

            var runtime = string.IsNullOrWhiteSpace(state.Session.PluginId)
                ? null
                : _pluginManager.GetRuntime(state.Session.PluginId);

            if (runtime is not null)
            {
                await _pluginHostProtocol.DisconnectAsync(
                    runtime,
                    sessionId,
                    reason,
                    TimeSpan.FromSeconds(5));
            }
            else
            {
                await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: reason);
                await _shmSessionService.ReleaseSegmentAsync(sessionId);
            }
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

    public async Task<PluginCommandResult> SendAsync(
        string sessionId,
        byte[] data,
        MessageFormat format = MessageFormat.Text,
        string? transmitTargetId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out _))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        data ??= Array.Empty<byte>();

        var transmitTargetAttributes = await TryGetTransmitTargetAttributesAsync(
            sessionId,
            transmitTargetId,
            cancellationToken);

        var result = await _pluginHostProtocol.SendDataAsync(
            sessionId,
            data,
            transmitTargetId,
            TimeSpan.FromSeconds(3),
            cancellationToken);

        if (!result.Ok)
        {
            return result;
        }

        result = result.BytesWritten > 0
            ? result
            : result with { BytesWritten = data.Length };

        // Mirror TX into the FrameStore so RX/TX share one timeline.
        _frameStore.Append(
            sessionId,
            DateTime.UtcNow,
            FrameDirection.Tx,
            data,
            format,
            source: "send-tx",
            attributes: transmitTargetAttributes);
        _eventBus.Publish(new DataSentEvent(sessionId, data, result.BytesWritten));
        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryGetTransmitTargetAttributesAsync(
        string sessionId,
        string? transmitTargetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transmitTargetId))
        {
            return null;
        }

        try
        {
            var targets = await _pluginHostProtocol.GetTransmitTargetsAsync(
                sessionId,
                TimeSpan.FromSeconds(1),
                cancellationToken);
            var target = targets.Targets.FirstOrDefault(item => string.Equals(item.Id, transmitTargetId, StringComparison.Ordinal));
            return target?.Attributes;
        }
        catch
        {
            return null;
        }
    }

    public async Task<PluginTransmitTargetSnapshot> GetTransmitTargetsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out _))
        {
            return new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }

        return await _pluginHostProtocol.GetTransmitTargetsAsync(
            sessionId,
            TimeSpan.FromSeconds(3),
            cancellationToken);
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
            DisplayIcon = descriptor.DisplayIcon,
            CanReconnect = descriptor.CanReconnect ?? true,
            InitializationState = descriptor.InitializationState,
            InitializationError = descriptor.InitializationError,
            ArchiveState = ResolveArchiveState(descriptor),
            ArchiveError = descriptor.ArchiveError,
            PayloadRenderMode = descriptor.PayloadRenderMode,
            DisplayDensity = descriptor.DisplayDensity,
            ParentSessionId = descriptor.ParentSessionId,
            ManagedResourceKinds = descriptor.ManagedResourceKinds,
            Status = SessionStatus.Disconnected
        };

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
            state.Session.DisplayIcon = session.DisplayIcon;
            state.Session.CanReconnect = session.CanReconnect;
            state.Session.InitializationState = session.InitializationState;
            state.Session.InitializationError = session.InitializationError;
            state.Session.ArchiveState = session.ArchiveState;
            state.Session.ArchiveError = session.ArchiveError;
            state.Session.PayloadRenderMode = session.PayloadRenderMode;
            state.Session.DisplayDensity = session.DisplayDensity;
            state.Session.ParentSessionId = session.ParentSessionId;
            state.Session.ManagedResourceKinds = session.ManagedResourceKinds;
            if (state.Session.Status != SessionStatus.Connected)
            {
                state.Session.Status = SessionStatus.Disconnected;
            }
        }

        _eventBus.Publish(new SessionCreatedEvent(state.Session));
    }

    public void UpdateSession(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_sessions.TryGetValue(session.Id, out var state))
        {
            return;
        }

        if (!ReferenceEquals(state.Session, session))
        {
            state.Session.Name = session.Name;
            state.Session.AdapterId = session.AdapterId;
            state.Session.PluginId = session.PluginId;
            state.Session.CapabilityId = session.CapabilityId;
            state.Session.ParametersJson = session.ParametersJson;
            state.Session.DisplayTitle = session.DisplayTitle;
            state.Session.DisplaySubtitle = session.DisplaySubtitle;
            state.Session.DisplayIcon = session.DisplayIcon;
            state.Session.CanReconnect = session.CanReconnect;
            state.Session.InitializationState = session.InitializationState;
            state.Session.InitializationError = session.InitializationError;
            state.Session.ArchiveState = session.ArchiveState;
            state.Session.ArchiveError = session.ArchiveError;
            state.Session.PayloadRenderMode = session.PayloadRenderMode;
            state.Session.DisplayDensity = session.DisplayDensity;
            state.Session.ParentSessionId = session.ParentSessionId;
            state.Session.ManagedResourceKinds = session.ManagedResourceKinds;
        }

        _eventBus.Publish(new SessionUpdatedEvent(state.Session));
    }

    private static SessionArchiveState ResolveArchiveState(SessionDescriptor descriptor)
        => descriptor.ArchiveState == SessionArchiveState.Disabled && descriptor.EnableDatabaseStorage
            ? SessionArchiveState.Enabled
            : descriptor.ArchiveState;

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

    private static void ApplyConnectResultMetadata(Session session, PluginConnectResult result)
    {
        if (result.CommittedParameters is { ValueKind: JsonValueKind.Object } committed)
        {
            session.ParametersJson = committed.GetRawText();
        }

        session.DisplayTitle = result.DisplayTitle;
        session.DisplaySubtitle = result.DisplaySubtitle;
        session.DisplayIcon = result.SessionIcon;
        if (result.CanReconnect is { } canReconnect)
        {
            session.CanReconnect = canReconnect;
        }
        session.ManagedResourceKinds = result.ManagedResourceKinds ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(result.ParentSessionId))
        {
            session.ParentSessionId = result.ParentSessionId;
        }
        else
        {
            session.ParentSessionId = null;
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

}
