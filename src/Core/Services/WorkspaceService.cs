using System.Text.Json;
using ComCross.Core.Models;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Workspace service that handles all business logic for serial port operations and workspace state management.
/// Decouples business logic from UI layer (View/ViewModel).
/// In v0.4+, manages Workload abstraction layer.
/// </summary>
public sealed class WorkspaceService
{
    private readonly DeviceService _deviceService;
    private readonly IMessageStreamService _messageStream;
    private readonly LogStorageService _logStorageService;
    private readonly NotificationService _notificationService;
    private readonly WorkspaceStateStore _workspaceStateStore;
    private readonly WorkloadService _workloadService;
    private readonly IEventBus _eventBus;

    private bool _sessionsRestored;

    public WorkspaceService(
        DeviceService deviceService,
        IMessageStreamService messageStream,
        LogStorageService logStorageService,
        NotificationService notificationService,
        WorkspaceStateStore workspaceStateStore,
        WorkloadService workloadService,
        IEventBus eventBus)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _logStorageService = logStorageService ?? throw new ArgumentNullException(nameof(logStorageService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _workspaceStateStore = workspaceStateStore ?? throw new ArgumentNullException(nameof(workspaceStateStore));
        _workloadService = workloadService ?? throw new ArgumentNullException(nameof(workloadService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// Get all active sessions.
    /// </summary>
    public Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        // Sessions are held by DeviceService in-memory.
        // CancellationToken is accepted for API symmetry.
        return Task.FromResult(_deviceService.GetAllSessions());
    }

    /// <summary>
    /// Connect to a device via plugin
    /// </summary>
    public async Task<Session> ConnectAsync(
        string pluginId,
        string capabilityId,
        string parametersJson,
        string? sessionName = null,
        string? scopeSessionId = null,
        string? resourceKind = null,
        string? resourceId = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"session-{Guid.NewGuid()}";
        var name = string.IsNullOrWhiteSpace(sessionName) ? null : sessionName;

        try
        {
            var parameters = JsonSerializer.Deserialize<JsonElement>(parametersJson);
            var session = await _deviceService.ConnectAsync(
                pluginId,
                capabilityId,
                sessionId,
                name,
                parameters,
                scopeSessionId,
                resourceKind,
                resourceId,
                cancellationToken);
            await _workloadService.AddSessionToActiveWorkloadIfMissingAsync(session.Id);
            _logStorageService.StartSession(session);
            return session;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Disconnect from a session
    /// </summary>
    public async Task DisconnectAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _deviceService.DisconnectAsync(sessionId);
            await _logStorageService.StopSessionAsync(sessionId);
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Send data to a session
    /// </summary>
    public async Task<int> SendDataAsync(string sessionId, byte[] data, CancellationToken cancellationToken = default)
    {
        return await _deviceService.SendAsync(sessionId, data, MessageFormat.Text, cancellationToken);
    }

    /// <summary>
    /// Send formatted message to a session (text or hex)
    /// </summary>
    public async Task<int> SendMessageAsync(string sessionId, string message, MessageFormat format, bool addCr, bool addLf, CancellationToken cancellationToken = default)
    {
        byte[] data = format == MessageFormat.Hex
            ? Convert.FromHexString(message.Replace(" ", ""))
            : System.Text.Encoding.UTF8.GetBytes(message);

        if (addCr || addLf)
        {
            var suffix = (addCr ? "\r" : "") + (addLf ? "\n" : "");
            var suffixBytes = System.Text.Encoding.UTF8.GetBytes(suffix);
            data = data.Concat(suffixBytes).ToArray();
        }

        return await _deviceService.SendAsync(sessionId, data, format, cancellationToken);
    }

    /// <summary>
    /// Clear messages for a session
    /// </summary>
    public void ClearMessages(string sessionId)
    {
        _messageStream.Clear(sessionId);
    }

    /// <summary>
    /// Get messages for a session
    /// </summary>
    public IReadOnlyList<LogMessage> GetMessages(string sessionId, int skip = 0, int take = 100)
    {
        return _messageStream.GetMessages(sessionId, skip, take);
    }

    /// <summary>
    /// Search messages in a session
    /// </summary>
    public IReadOnlyList<LogMessage> SearchMessages(string sessionId, string query, bool isRegex = false)
    {
        return _messageStream.Search(sessionId, query, isRegex);
    }

    /// <summary>
    /// Subscribe to messages for a session
    /// </summary>
    public IDisposable SubscribeToMessages(string sessionId, Action<LogMessage> handler)
    {
        return _messageStream.Subscribe(sessionId, handler);
    }

    /// <summary>
    /// Get session information
    /// </summary>
    public Session? GetSession(string sessionId)
    {
        return _deviceService.GetSession(sessionId);
    }

    /// <summary>
    /// Get all active sessions
    /// </summary>
    public IReadOnlyList<Session> GetAllSessions()
    {
        return _deviceService.GetAllSessions();
    }

    /// <summary>
    /// Save current workspace state to persistent storage.
    /// In v0.4+, this includes Workload information.
    /// </summary>
    public async Task SaveCurrentStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll = true, CancellationToken cancellationToken = default)
    {
        await BuildWorkspaceStateAsync(sessions, activeSession, autoScroll, cancellationToken);
    }
    
    /// <summary>
    /// Load workspace state from persistent storage.
    /// Performs migration if needed (v0.3 → v0.4).
    /// </summary>
    public async Task<WorkspaceState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _workspaceStateStore.LoadAsync(cancellationToken);

        // Restore persisted sessions as disconnected (no auto-reconnect).
        if (!_sessionsRestored)
        {
            RestoreSessionsFromState(state);
            _sessionsRestored = true;
        }
        
        return state;
    }

    private void RestoreSessionsFromState(WorkspaceState state)
    {
        // Prefer v0.4+ descriptors.
        if (state.SessionDescriptors is { Count: > 0 })
        {
            foreach (var descriptor in state.SessionDescriptors)
            {
                _deviceService.RestoreSession(descriptor);
            }

            return;
        }

        // Legacy v0.3 sessions: best-effort map to serial.adapter/serial.
        #pragma warning disable CS0618 // Type or member is obsolete
        if (state.Sessions is { Count: > 0 })
        {
            foreach (var legacy in state.Sessions)
            {
                if (string.IsNullOrWhiteSpace(legacy.Id))
                {
                    continue;
                }

                var parameters = new
                {
                    port = legacy.Port,
                    baudRate = legacy.Settings.BaudRate,
                    dataBits = legacy.Settings.DataBits,
                    parity = legacy.Settings.Parity.ToString(),
                    stopBits = legacy.Settings.StopBits.ToString(),
                    flowControl = legacy.Settings.FlowControl.ToString()
                };

                state.SessionDescriptors.Add(new SessionDescriptor
                {
                    Id = legacy.Id,
                    Name = legacy.Name,
                    AdapterId = "plugin:serial.adapter:serial",
                    PluginId = "serial.adapter",
                    CapabilityId = "serial",
                    ParametersJson = System.Text.Json.JsonSerializer.Serialize(parameters)
                });
            }

            foreach (var descriptor in state.SessionDescriptors)
            {
                _deviceService.RestoreSession(descriptor);
            }
        }
        #pragma warning restore CS0618
    }
    
    /// <summary>
    /// Save workspace state to persistent storage (internal method).
    /// </summary>
    public async Task SaveStateAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        await _workspaceStateStore.SaveAsync(state, cancellationToken);
    }

    /// <summary>
    /// Delete a session (disconnect if connected and clear messages)
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var orderedSessionIds = CollectSessionDeletionOrder(_deviceService.GetAllSessions(), sessionId);
        if (orderedSessionIds.Count == 0)
        {
            return;
        }

        foreach (var deleteId in orderedSessionIds)
        {
            var session = _deviceService.GetSession(deleteId);
            if (session != null && session.Status == SessionStatus.Connected)
            {
                await DisconnectAsync(deleteId, cancellationToken);
            }

            _deviceService.RemoveSession(deleteId);
            ClearMessages(deleteId);
            await _workloadService.RemoveSessionFromAllWorkloadsAsync(deleteId);
        }

        await _workspaceStateStore.UpdateAsync(state =>
        {
            state.SessionDescriptors.RemoveAll(descriptor =>
                orderedSessionIds.Contains(descriptor.Id, StringComparer.Ordinal));

            if (state.UiState?.ActiveSessionId is { Length: > 0 } activeSessionId
                && orderedSessionIds.Contains(activeSessionId, StringComparer.Ordinal))
            {
                state.UiState.ActiveSessionId = null;
            }
        }, cancellationToken);

        foreach (var deleteId in orderedSessionIds)
        {
            _eventBus.Publish(new SessionDeletedEvent(deleteId));
        }
    }

    /// <summary>
    /// Build workspace state from current sessions and workloads.
    /// In v0.4+, includes Workload information with session associations.
    /// </summary>
    private Task BuildWorkspaceStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll, CancellationToken cancellationToken)
    {
        var descriptors = sessions.Select(s => new SessionDescriptor
        {
            Id = s.Id,
            Name = s.Name,
            AdapterId = s.AdapterId,
            PluginId = s.PluginId,
            CapabilityId = s.CapabilityId,
            ParametersJson = s.ParametersJson,
            EnableDatabaseStorage = s.EnableDatabaseStorage,
            Kind = s.Kind,
            ParentSessionId = s.ParentSessionId
        }).ToList();

        return _workspaceStateStore.UpdateAsync(state =>
        {
            state.SessionDescriptors = descriptors;
            state.UiState ??= new UiState();
            state.UiState.ActiveSessionId = activeSession?.Id;
            state.UiState.AutoScroll = autoScroll;
        }, cancellationToken);
    }

    private static List<string> CollectSessionDeletionOrder(IEnumerable<Session> sessions, string rootSessionId)
    {
        var byParent = sessions.ToLookup(session => session.ParentSessionId, StringComparer.Ordinal);
        var ordered = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string sessionId)
        {
            if (!visited.Add(sessionId))
            {
                return;
            }

            foreach (var child in byParent[sessionId])
            {
                Visit(child.Id);
            }

            ordered.Add(sessionId);
        }

        Visit(rootSessionId);
        return ordered;
    }
}
