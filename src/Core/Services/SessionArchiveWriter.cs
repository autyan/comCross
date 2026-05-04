using System.Collections.Concurrent;
using System.Threading.Channels;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed record SessionArchiveWriteFailedCoreEvent(
    string SessionId,
    string Error);

public sealed class SessionArchiveWriter : IDisposable
{
    private static readonly TimeSpan NotificationThrottle = TimeSpan.FromMinutes(5);
    private const int QueueCapacity = 4096;

    private readonly ISessionArchiveStore _archiveStore;
    private readonly SessionArchiveStateTracker _stateTracker;
    private readonly NotificationService _notificationService;
    private readonly IStorageHealthService _storageHealth;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SessionArchiveWriter> _logger;
    private readonly Channel<MessageFrameRecord> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationUtc = new(StringComparer.Ordinal);
    private readonly Task _writerTask;

    public SessionArchiveWriter(
        ISessionArchiveStore archiveStore,
        SessionArchiveStateTracker stateTracker,
        NotificationService notificationService,
        IStorageHealthService storageHealth,
        IEventBus eventBus,
        ILogger<SessionArchiveWriter> logger)
    {
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _storageHealth = storageHealth ?? throw new ArgumentNullException(nameof(storageHealth));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queue = Channel.CreateBounded<MessageFrameRecord>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = Task.Run(ProcessAsync);
    }

    public void EnqueueIfEnabled(MessageFrameRecord frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!_stateTracker.IsEnabled(frame.SessionId))
        {
            return;
        }

        if (!_queue.Writer.TryWrite(frame))
        {
            _ = ReportFailureAsync(frame.SessionId, "archive queue is full", CancellationToken.None);
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var frame in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                if (!_stateTracker.IsEnabled(frame.SessionId))
                {
                    continue;
                }

                try
                {
                    _archiveStore.Append(frame);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Session archive write failed: {SessionId}", frame.SessionId);
                    await ReportFailureAsync(frame.SessionId, ex.Message, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReportFailureAsync(string sessionId, string error, CancellationToken cancellationToken)
    {
        _eventBus.Publish(new SessionArchiveWriteFailedCoreEvent(sessionId, error));
        _ = _storageHealth.ReportAsync(StorageHealth.ArchiveError, error, NotificationLevel.Warning, CancellationToken.None);

        var key = $"{sessionId}:{error}";
        var now = DateTime.UtcNow;
        if (_lastNotificationUtc.TryGetValue(key, out var last) && now - last < NotificationThrottle)
        {
            return;
        }

        _lastNotificationUtc[key] = now;

        try
        {
            await _notificationService.AddAsync(
                NotificationCategory.Storage,
                NotificationLevel.Warning,
                "notification.storage.archiveWriteFailed",
                [sessionId, error],
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to add archive write failure notification.");
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        _queue.Writer.TryComplete();

        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
