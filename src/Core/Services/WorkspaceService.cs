using ComCross.Core.Models;
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
    private readonly ConfigService _configService;
    private readonly WorkloadService _workloadService;
    private readonly WorkspaceMigrationService _migrationService;

    public WorkspaceService(
        DeviceService deviceService,
        IMessageStreamService messageStream,
        LogStorageService logStorageService,
        NotificationService notificationService,
        ConfigService configService,
        WorkloadService workloadService,
        WorkspaceMigrationService migrationService)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _logStorageService = logStorageService ?? throw new ArgumentNullException(nameof(logStorageService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _workloadService = workloadService ?? throw new ArgumentNullException(nameof(workloadService));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
    }

    /// <summary>
    /// List all available serial devices
    /// </summary>
    public async Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await _deviceService.ListDevicesAsync(cancellationToken);
    }

    /// <summary>
    /// Connect to a serial port
    /// </summary>
    public async Task<Session> ConnectAsync(string port, SerialSettings settings, string? sessionName = null, CancellationToken cancellationToken = default)
    {
        var sessionId = $"session-{Guid.NewGuid()}";
        var name = sessionName ?? port;

        try
        {
            var session = await _deviceService.ConnectAsync(sessionId, port, name, settings, cancellationToken);
            _logStorageService.StartSession(session);
            return session;
        }
        catch (Exception)
        {
            // Let the exception bubble up, but could add logging here
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
        var state = await BuildWorkspaceStateAsync(sessions, activeSession, autoScroll);
        await SaveStateAsync(state, cancellationToken);
    }
    
    /// <summary>
    /// Load workspace state from persistent storage.
    /// Performs migration if needed (v0.3 → v0.4).
    /// </summary>
    public async Task<WorkspaceState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _configService.LoadWorkspaceStateAsync(cancellationToken);
        
        if (state == null)
        {
            // First run: create default workspace with default workload
            state = new WorkspaceState();
            state.EnsureDefaultWorkload();
            await SaveStateAsync(state, cancellationToken);
            return state;
        }
        
        // Check if migration is needed
        if (_migrationService.NeedsMigration(state))
        {
            state = _migrationService.Migrate(state);
            await SaveStateAsync(state, cancellationToken);
        }
        
        // Ensure default workload exists
        state.EnsureDefaultWorkload();
        
        return state;
    }
    
    /// <summary>
    /// Save workspace state to persistent storage (internal method).
    /// </summary>
    public async Task SaveStateAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        await _configService.SaveWorkspaceStateAsync(state, cancellationToken);
    }

    /// <summary>
    /// Delete a session (disconnect if connected and clear messages)
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _deviceService.GetSession(sessionId);
        if (session != null && session.Status == SessionStatus.Connected)
        {
            await DisconnectAsync(sessionId, cancellationToken);
        }
        
        ClearMessages(sessionId);
    }

    /// <summary>
    /// Build workspace state from current sessions and workloads.
    /// In v0.4+, includes Workload information with session associations.
    /// </summary>
    private async Task<WorkspaceState> BuildWorkspaceStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll)
    {
        // Get current workloads from WorkloadService
        var workloads = await _workloadService.GetAllWorkloadsAsync();
        
        // Build session state with WorkloadId for recovery
        var sessionStates = sessions.Select(s => new SessionState
        {
            Id = s.Id,
            Port = s.Port,
            Name = s.Name,
            Settings = s.Settings,
            Connected = s.Status == SessionStatus.Connected,
            Metrics = new MetricsState
            {
                Rx = s.RxBytes,
                Tx = s.TxBytes
            },
            // Associate session with workload for data recovery
            WorkloadId = FindWorkloadForSession(workloads, s.Id)
        }).ToList();
        
        return new WorkspaceState
        {
            Version = "0.4.0",
            WorkspaceId = "default",
            Workloads = workloads,
            UiState = new UiState
            {
                ActiveSessionId = activeSession?.Id,
                AutoScroll = autoScroll
            }
        };
    }
    
    /// <summary>
    /// Find which workload contains the given session ID.
    /// </summary>
    private string? FindWorkloadForSession(List<Workload> workloads, string sessionId)
    {
        return workloads.FirstOrDefault(w => w.SessionIds.Contains(sessionId))?.Id;
    }
}
