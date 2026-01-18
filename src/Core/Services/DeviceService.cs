using System.Collections.Concurrent;
using System.Threading.Channels;
using ComCross.PluginSdk;
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
    private readonly IDeviceAdapter _adapter;
    private readonly IEventBus _eventBus;
    private readonly IMessageStreamService _messageStream;
    private readonly NotificationService _notificationService;
    private readonly SharedMemoryManager _sharedMemoryManager;
    private readonly SharedMemoryReader _sharedMemoryReader;
    private readonly SharedMemoryConfig _sharedMemoryConfig;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    private readonly CancellationTokenSource _sharedMemoryConsumeCts = new();
    private readonly Task _sharedMemoryConsumeTask;

    public DeviceService(
        IDeviceAdapter adapter,
        IEventBus eventBus,
        IMessageStreamService messageStream,
        NotificationService notificationService,
        SharedMemoryManager sharedMemoryManager,
        SharedMemoryReader sharedMemoryReader,
        SharedMemoryConfig sharedMemoryConfig)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
        _sharedMemoryReader = sharedMemoryReader ?? throw new ArgumentNullException(nameof(sharedMemoryReader));
        _sharedMemoryConfig = sharedMemoryConfig ?? throw new ArgumentNullException(nameof(sharedMemoryConfig));

        _sharedMemoryManager.Initialize();
        _sharedMemoryManager.BackpressureDetected += HandleBackpressureDetected;

        _sharedMemoryConsumeTask = Task.Run(() => ConsumeSharedMemoryFramesAsync(_sharedMemoryConsumeCts.Token));
    }

    public async Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await _adapter.ListDevicesAsync(cancellationToken);
    }

    public async Task<Session> ConnectAsync(
        string sessionId,
        string port,
        string name,
        SerialSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId cannot be empty.", nameof(sessionId));
        }
        ArgumentException.ThrowIfNullOrEmpty(port);
        ArgumentNullException.ThrowIfNull(settings);

        var session = new Session
        {
            Id = sessionId,
            Name = name,
            Port = port,
            BaudRate = settings.BaudRate,
            Status = SessionStatus.Connecting,
            Settings = settings
        };

        var connection = _adapter.OpenConnection(port);

        SessionSegment? segment = null;
        if (connection is ISharedMemoryCapableConnection shmCapable)
        {
            segment = await _sharedMemoryManager.AllocateSegmentAsync(sessionId, _sharedMemoryConfig.DefaultSegmentSize);
            shmCapable.SetSharedMemorySegment(segment);
            shmCapable.SetBackpressureLevel(BackpressureLevel.None);
            _sharedMemoryReader.StartReading(sessionId, segment);
        }

        connection.DataReceived += (_, data) => OnDataReceived(sessionId, data);
        connection.ErrorOccurred += (_, error) => OnErrorOccurred(sessionId, error);

        try
        {
            await connection.OpenAsync(settings, cancellationToken);
            session.Status = SessionStatus.Connected;
            session.StartTime = DateTime.UtcNow;

            _sessions[sessionId] = new SessionState(session, connection);
            _eventBus.Publish(new DeviceConnectedEvent(sessionId, port));

            return session;
        }
        catch (SerialPortAccessDeniedException ex)
        {
            connection.Dispose();
            if (segment is not null)
            {
                await _sharedMemoryReader.StopReadingAsync(sessionId);
                _sharedMemoryManager.ReleaseSegment(sessionId);
            }
            
            // Notify user about permission issue
            await _notificationService.AddAsync(
                NotificationCategory.Connection,
                NotificationLevel.Warning,
                "notification.permission.denied",
                new object[] { ex.PortPath },
                cancellationToken);
            
            throw;
        }
        catch
        {
            connection.Dispose();
            if (segment is not null)
            {
                await _sharedMemoryReader.StopReadingAsync(sessionId);
                _sharedMemoryManager.ReleaseSegment(sessionId);
            }
            throw;
        }
    }

    public async Task DisconnectAsync(string sessionId, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId cannot be empty.", nameof(sessionId));
        }

        if (_sessions.TryRemove(sessionId, out var state))
        {
            await state.Connection.CloseAsync();
            state.Connection.Dispose();

            await _sharedMemoryReader.StopReadingAsync(sessionId);
            _sharedMemoryManager.ReleaseSegment(sessionId);

            _eventBus.Publish(new DeviceDisconnectedEvent(sessionId, state.Session.Port, reason));
        }
    }

    private async Task ConsumeSharedMemoryFramesAsync(CancellationToken cancellationToken)
    {
        ChannelReader<PhysicalFrame> reader = _sharedMemoryReader.GetFrameReader();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var frame = await reader.ReadAsync(cancellationToken);
                OnPhysicalFrameReceived(frame.SessionId, frame.Data);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private void OnPhysicalFrameReceived(string sessionId, byte[] data)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return;
        }

        state.Session.RxBytes += data.Length;

        var format = MessageFormat.Text;
        var content = System.Text.Encoding.UTF8.GetString(data);
        if (content.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
        {
            format = MessageFormat.Hex;
            content = BitConverter.ToString(data).Replace("-", " ");
        }

        var message = new LogMessage
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Content = content,
            Level = LogLevel.Info,
            Source = "RX",
            RawData = data,
            Format = format
        };

        _messageStream.Append(sessionId, message);
        _eventBus.Publish(new DataReceivedEvent(sessionId, data, data.Length));
    }

    private void HandleBackpressureDetected(string sessionId, BackpressureLevel level)
    {
        if (_sessions.TryGetValue(sessionId, out var state) && state.Connection is ISharedMemoryCapableConnection shmCapable)
        {
            shmCapable.SetBackpressureLevel(level);
        }
    }

    public async Task<int> SendAsync(string sessionId, byte[] data, MessageFormat format = MessageFormat.Text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId cannot be empty.", nameof(sessionId));
        }
        ArgumentNullException.ThrowIfNull(data);

        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        var bytesSent = await state.Connection.WriteAsync(data, cancellationToken);
        state.Session.TxBytes += bytesSent;

        // Add sent message to stream with proper formatting
        var content = format == MessageFormat.Hex
            ? BitConverter.ToString(data).Replace("-", " ")
            : System.Text.Encoding.UTF8.GetString(data);

        var sentMessage = new LogMessage
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Content = content,
            Level = LogLevel.Info,
            Source = "TX",
            RawData = data,
            Format = format
        };
        _messageStream.Append(sessionId, sentMessage);

        _eventBus.Publish(new DataSentEvent(sessionId, data, bytesSent));

        return bytesSent;
    }

    public Session? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var state) ? state.Session : null;
    }

    public IReadOnlyList<Session> GetAllSessions()
    {
        return _sessions.Values.Select(s => s.Session).ToList();
    }

    private void OnDataReceived(string sessionId, byte[] data)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.Session.RxBytes += data.Length;

            // Try to decode as UTF-8 text, fall back to hex if not valid
            var format = MessageFormat.Text;
            var content = System.Text.Encoding.UTF8.GetString(data);
            
            // Check if the decoded string contains unprintable characters
            if (content.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
            {
                format = MessageFormat.Hex;
                content = BitConverter.ToString(data).Replace("-", " ");
            }

            var message = new LogMessage
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Content = content,
                Level = LogLevel.Info,
                Source = "RX",
                RawData = data,
                Format = format
            };

            _messageStream.Append(sessionId, message);
            _eventBus.Publish(new DataReceivedEvent(sessionId, data, data.Length));
        }
    }

    private void OnErrorOccurred(string sessionId, string error)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            var message = new LogMessage
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Content = $"Error: {error}",
                Level = LogLevel.Error,
                Source = "System"
            };

            _messageStream.Append(sessionId, message);
        }
    }

    public void Dispose()
    {
        try
        {
            _sharedMemoryConsumeCts.Cancel();
            _sharedMemoryConsumeTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _sharedMemoryManager.BackpressureDetected -= HandleBackpressureDetected;

        foreach (var state in _sessions.Values)
        {
            state.Connection.Dispose();
        }
        _sessions.Clear();

        try
        {
            _sharedMemoryConsumeCts.Dispose();
        }
        catch
        {
        }
    }

    private sealed record SessionState(Session Session, IDeviceConnection Connection);
}
