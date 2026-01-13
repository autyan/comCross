using System.Collections.Concurrent;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Manages device connections and sessions
/// </summary>
public sealed class DeviceService : IDisposable
{
    private readonly IDeviceAdapter _adapter;
    private readonly IEventBus _eventBus;
    private readonly IMessageStreamService _messageStream;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    public DeviceService(
        IDeviceAdapter adapter,
        IEventBus eventBus,
        IMessageStreamService messageStream)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
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
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
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
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync(string sessionId, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        if (_sessions.TryRemove(sessionId, out var state))
        {
            await state.Connection.CloseAsync();
            state.Connection.Dispose();

            _eventBus.Publish(new DeviceDisconnectedEvent(sessionId, state.Session.Port, reason));
        }
    }

    public async Task<int> SendAsync(string sessionId, byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(data);

        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        var bytesSent = await state.Connection.WriteAsync(data, cancellationToken);
        state.Session.TxBytes += bytesSent;

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

            var message = new LogMessage
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Content = System.Text.Encoding.UTF8.GetString(data),
                Level = LogLevel.Info,
                Source = state.Session.Port,
                RawData = data
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
        foreach (var state in _sessions.Values)
        {
            state.Connection.Dispose();
        }
        _sessions.Clear();
    }

    private sealed record SessionState(Session Session, IDeviceConnection Connection);
}
