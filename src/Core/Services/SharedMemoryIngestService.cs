using System.Collections.Concurrent;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Single-task, batched shared-memory ingest loop.
/// Replaces per-session polling tasks.
/// </summary>
public sealed class SharedMemoryIngestService : IDisposable
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMilliseconds(2),
        TimeSpan.FromMilliseconds(5),
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
    ];

    private const int MaxDrainPerSessionPerRound = 128;

    private readonly ILogger<SharedMemoryIngestService> _logger;
    private readonly IFrameStore _frameStore;
    private readonly IEventBus _eventBus;

    private readonly ConcurrentDictionary<string, SessionSegment> _segments = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private int _started;

    public SharedMemoryIngestService(
        IFrameStore frameStore,
        IEventBus eventBus,
        ILogger<SharedMemoryIngestService> logger)
    {
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Start();
    }

    public void Register(string sessionId, SessionSegment segment)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || segment is null)
        {
            return;
        }

        _segments[sessionId] = segment;
    }

    public void Unregister(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _segments.TryRemove(sessionId, out _);
    }

    private void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _loop = Task.Run(LoopAsync);
        _logger.LogInformation("SharedMemoryIngestService started.");
    }

    private async Task LoopAsync()
    {
        var backoffIndex = 0;

        while (!_cts.IsCancellationRequested)
        {
            var hadActivity = false;

            try
            {
                foreach (var kvp in _segments)
                {
                    var sessionId = kvp.Key;
                    var segment = kvp.Value;

                    var drained = 0;
                    while (drained < MaxDrainPerSessionPerRound && segment.TryReadFrameRecord(out var record))
                    {
                        drained++;
                        hadActivity = true;

                        _frameStore.Append(
                            sessionId,
                            record.TimestampUtc,
                            FrameDirection.Rx,
                            record.RawData,
                            MessageFormat.Hex,
                            source: "shm-rx");

                        _eventBus.Publish(new DataReceivedEvent(sessionId, record.RawData, record.RawData.Length));
                    }
                }

                if (hadActivity)
                {
                    backoffIndex = 0;
                    // Yield to avoid starving other work.
                    await Task.Yield();
                }
                else
                {
                    var delay = Backoff[Math.Min(backoffIndex, Backoff.Length - 1)];
                    if (backoffIndex < Backoff.Length - 1)
                    {
                        backoffIndex++;
                    }

                    await Task.Delay(delay, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SharedMemoryIngestService loop error.");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), _cts.Token);
                }
                catch
                {
                }
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
