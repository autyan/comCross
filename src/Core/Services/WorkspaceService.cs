using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Workspace service that handles all business logic for serial port operations
/// Decouples business logic from UI layer (View/ViewModel)
/// </summary>
public sealed class WorkspaceService
{
    private readonly DeviceService _deviceService;
    private readonly IMessageStreamService _messageStream;
    private readonly LogStorageService _logStorageService;
    private readonly NotificationService _notificationService;
    private readonly ConfigService _configService;

    public WorkspaceService(
        DeviceService deviceService,
        IMessageStreamService messageStream,
        LogStorageService logStorageService,
        NotificationService notificationService,
        ConfigService configService)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _logStorageService = logStorageService ?? throw new ArgumentNullException(nameof(logStorageService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
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
    /// Save current workspace state to persistent storage
    /// </summary>
    public async Task SaveCurrentStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll = true, CancellationToken cancellationToken = default)
    {
        var state = BuildWorkspaceState(sessions, activeSession, autoScroll);
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

    private WorkspaceState BuildWorkspaceState(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll)
    {
        return new WorkspaceState
        {
            WorkspaceId = "default",
            Sessions = sessions.Select(s => new SessionState
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
                }
            }).ToList(),
            UiState = new UiState
            {
                ActiveSessionId = activeSession?.Id,
                AutoScroll = autoScroll
            }
        };
    }
}
