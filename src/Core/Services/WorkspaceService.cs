using System.Text.Json;
using ComCross.Core.Models;
using ComCross.PluginSdk;
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
    private readonly NotificationService _notificationService;
    private readonly WorkspaceStateStore _workspaceStateStore;
    private readonly WorkloadService _workloadService;
    private readonly PluginSessionInitializationService _sessionInitializationService;
    private readonly SessionDataCleanupService _sessionDataCleanupService;
    private readonly ISessionArchiveStore _archiveStore;
    private readonly IEventBus _eventBus;

    private bool _sessionsRestored;

    public WorkspaceService(
        DeviceService deviceService,
        IMessageStreamService messageStream,
        NotificationService notificationService,
        WorkspaceStateStore workspaceStateStore,
        WorkloadService workloadService,
        PluginSessionInitializationService sessionInitializationService,
        SessionDataCleanupService sessionDataCleanupService,
        ISessionArchiveStore archiveStore,
        IEventBus eventBus)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _workspaceStateStore = workspaceStateStore ?? throw new ArgumentNullException(nameof(workspaceStateStore));
        _workloadService = workloadService ?? throw new ArgumentNullException(nameof(workloadService));
        _sessionInitializationService = sessionInitializationService ?? throw new ArgumentNullException(nameof(sessionInitializationService));
        _sessionDataCleanupService = sessionDataCleanupService ?? throw new ArgumentNullException(nameof(sessionDataCleanupService));
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _eventBus.Subscribe<SessionArchiveWriteFailedCoreEvent>(evt => _ = SetSessionArchiveStateAsync(evt.SessionId, SessionArchiveState.Error, evt.Error));
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
        var parameters = JsonSerializer.Deserialize<JsonElement>(parametersJson);
        var sessionId = $"session-{Guid.NewGuid()}";
        var name = string.IsNullOrWhiteSpace(sessionName) ? null : sessionName;

        try
        {
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

            // DeviceService publishes SessionCreatedEvent before workspace membership is updated.
            // Publish a second upsert after membership so filtered session lists can include it immediately.
            _eventBus.Publish(new SessionCreatedEvent(session));
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
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Send data to a session
    /// </summary>
    public async Task<PluginCommandResult> SendDataAsync(
        string sessionId,
        byte[] data,
        string? transmitTargetId = null,
        CancellationToken cancellationToken = default)
    {
        return await _deviceService.SendAsync(sessionId, data, MessageFormat.Text, transmitTargetId, cancellationToken);
    }

    /// <summary>
    /// Send formatted message to a session (text or hex)
    /// </summary>
    public async Task<PluginCommandResult> SendMessageAsync(
        string sessionId,
        string message,
        MessageFormat format,
        bool addCr,
        bool addLf,
        string? transmitTargetId = null,
        CancellationToken cancellationToken = default)
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

        return await _deviceService.SendAsync(sessionId, data, format, transmitTargetId, cancellationToken);
    }

    public Task<PluginTransmitTargetSnapshot> GetTransmitTargetsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
        => _deviceService.GetTransmitTargetsAsync(sessionId, cancellationToken);

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
    /// </summary>
    public async Task<WorkspaceState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _workspaceStateStore.LoadAsync(cancellationToken);

        // Restore persisted sessions as disconnected (no auto-reconnect).
        if (!_sessionsRestored)
        {
            RestoreSessionsFromState(state);
            _sessionsRestored = true;
            await _sessionInitializationService.InitializeRestoredSessionsAsync(cancellationToken);
        }
        
        return state;
    }

    private void RestoreSessionsFromState(WorkspaceState state)
    {
        if (state.SessionDescriptors is not { Count: > 0 })
        {
            return;
        }

        foreach (var descriptor in state.SessionDescriptors)
        {
            _deviceService.RestoreSession(descriptor);
        }
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

        var cleanupTargets = orderedSessionIds
            .Select(deleteId =>
            {
                var session = _deviceService.GetSession(deleteId);
                return new SessionDataCleanupTarget(deleteId, session?.PluginId);
            })
            .ToList();

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

        await _sessionDataCleanupService.DeleteSessionOwnedDataAsync(cleanupTargets, cancellationToken);

        foreach (var deleteId in orderedSessionIds)
        {
            _eventBus.Publish(new SessionDeletedEvent(deleteId));
        }
    }

    /// <summary>
    /// Rename a session and persist the descriptor name.
    /// </summary>
    public async Task RenameSessionAsync(string sessionId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var session = _deviceService.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        var trimmedName = name.Trim();
        if (string.Equals(session.Name, trimmedName, StringComparison.Ordinal))
        {
            return;
        }

        session.Name = trimmedName;

        await _workspaceStateStore.UpdateAsync(state =>
        {
            var descriptor = state.SessionDescriptors.FirstOrDefault(d => string.Equals(d.Id, sessionId, StringComparison.Ordinal));
            if (descriptor is not null)
            {
                descriptor.Name = trimmedName;
            }
        }, cancellationToken);

        _eventBus.Publish(new SessionRenamedEvent(sessionId, trimmedName));
    }

    public async Task SetSessionArchiveStateAsync(
        string sessionId,
        SessionArchiveState archiveState,
        string? archiveError = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var session = _deviceService.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        session.ArchiveState = archiveState;
        session.ArchiveError = archiveState == SessionArchiveState.Error ? archiveError : null;

        await _workspaceStateStore.UpdateAsync(state =>
        {
            var descriptor = state.SessionDescriptors.FirstOrDefault(d => string.Equals(d.Id, sessionId, StringComparison.Ordinal));
            if (descriptor is null)
            {
                return;
            }

            descriptor.ArchiveState = session.ArchiveState;
            descriptor.ArchiveError = session.ArchiveError;
            descriptor.EnableDatabaseStorage = false;
        }, cancellationToken);

        _eventBus.Publish(new SessionUpdatedEvent(session));
    }

    public async Task SetSessionDisplayOptionsAsync(
        string sessionId,
        PayloadRenderMode payloadRenderMode,
        MessageDisplayDensity displayDensity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var session = _deviceService.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        if (session.PayloadRenderMode == payloadRenderMode && session.DisplayDensity == displayDensity)
        {
            return;
        }

        session.PayloadRenderMode = payloadRenderMode;
        session.DisplayDensity = displayDensity;

        await _workspaceStateStore.UpdateAsync(state =>
        {
            var descriptor = state.SessionDescriptors.FirstOrDefault(d => string.Equals(d.Id, sessionId, StringComparison.Ordinal));
            if (descriptor is null)
            {
                return;
            }

            descriptor.PayloadRenderMode = session.PayloadRenderMode;
            descriptor.DisplayDensity = session.DisplayDensity;
        }, cancellationToken);

        _eventBus.Publish(new SessionUpdatedEvent(session));
    }

    public bool HasSessionArchiveData(string sessionId)
        => _archiveStore.HasArchive(sessionId);

    public async Task DeleteSessionArchiveDataAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var session = _deviceService.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        _archiveStore.Delete(sessionId);
        await SetSessionArchiveStateAsync(sessionId, SessionArchiveState.Disabled, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Build workspace state from current sessions and workloads.
    /// In v0.4+, includes Workload information with session associations.
    /// </summary>
    private Task BuildWorkspaceStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll, CancellationToken cancellationToken)
    {
        var sessionDescriptors = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => new SessionDescriptor
            {
                Id = s.Id,
                Name = s.Name,
                AdapterId = s.AdapterId,
                PluginId = s.PluginId,
                CapabilityId = s.CapabilityId,
                ParametersJson = s.ParametersJson,
                DisplayTitle = s.DisplayTitle,
                DisplaySubtitle = s.DisplaySubtitle,
                DisplayIcon = s.DisplayIcon,
                CanReconnect = s.CanReconnect,
                CanTransmit = s.CanTransmit,
                InitializationState = s.InitializationState,
                InitializationError = s.InitializationError,
                ArchiveState = s.ArchiveState,
                ArchiveError = s.ArchiveError,
                PayloadRenderMode = s.PayloadRenderMode,
                DisplayDensity = s.DisplayDensity,
                ParentSessionId = s.ParentSessionId,
                ManagedResourceKinds = s.ManagedResourceKinds.ToList()
            })
            .ToList();

        return _workspaceStateStore.UpdateAsync(state =>
        {
            var runtimeIds = sessionDescriptors.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            var existingById = state.SessionDescriptors
                .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                .ToDictionary(d => d.Id, StringComparer.Ordinal);
            var merged = new List<SessionDescriptor>(sessionDescriptors.Count + state.SessionDescriptors.Count);

            // The caller-provided session sequence is the current UI/runtime order and is authoritative.
            foreach (var descriptor in sessionDescriptors)
            {
                if (existingById.TryGetValue(descriptor.Id, out var existing))
                {
                    descriptor.LastInitializedPluginVersion = existing.LastInitializedPluginVersion;
                    descriptor.StorageSchemaVersion = existing.StorageSchemaVersion;
                }
            }
            merged.AddRange(sessionDescriptors);

            // Preserve descriptors that are not currently materialized in DeviceService, without letting
            // hidden workload sessions disturb the visible session order.
            foreach (var existing in state.SessionDescriptors)
            {
                if (!string.IsNullOrWhiteSpace(existing.Id) && !runtimeIds.Contains(existing.Id))
                {
                    merged.Add(existing);
                }
            }

            state.SessionDescriptors = merged;

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
