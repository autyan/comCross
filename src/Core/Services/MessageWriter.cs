using System.Threading.Channels;

namespace ComCross.Core.Services;

/// <summary>
/// High-performance message writer with Channel-based batch writing.
/// Implements producer-consumer pattern for SQLite batch inserts.
/// </summary>
public sealed class MessageWriter : IDisposable, IAsyncDisposable
{
    private readonly WorkspaceDatabaseService _database;
    private readonly Channel<RawMessage> _messageChannel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts;
    
    private readonly int _batchSize;
    private readonly TimeSpan _batchTimeout;

    private long _totalMessagesWritten;
    private long _totalBatchesWritten;

    public MessageWriter(
        WorkspaceDatabaseService database,
        int batchSize = 100,
        int batchTimeoutMs = 100,
        int channelCapacity = 10000)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _batchSize = batchSize;
        _batchTimeout = TimeSpan.FromMilliseconds(batchTimeoutMs);

        // Create bounded channel with backpressure
        _messageChannel = Channel.CreateBounded<RawMessage>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _cts = new CancellationTokenSource();
        _writerTask = Task.Run(() => WriterLoop(_cts.Token));
    }

    /// <summary>
    /// Queues a message for async batch writing to database.
    /// </summary>
    public async ValueTask WriteAsync(
        string sessionId,
        long timestamp,
        string direction,
        byte[] rawData,
        CancellationToken cancellationToken = default)
    {
        var message = new RawMessage
        {
            SessionId = sessionId,
            Timestamp = timestamp,
            Direction = direction,
            RawData = rawData
        };

        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    /// <summary>
    /// Gets writer statistics.
    /// </summary>
    public WriterStatistics GetStatistics()
    {
        return new WriterStatistics
        {
            TotalMessagesWritten = Interlocked.Read(ref _totalMessagesWritten),
            TotalBatchesWritten = Interlocked.Read(ref _totalBatchesWritten),
            QueuedMessages = _messageChannel.Reader.Count,
            ChannelCapacity = 10000
        };
    }

    private async Task WriterLoop(CancellationToken cancellationToken)
    {
        var batch = new List<RawMessage>(_batchSize);
        
        try
        {
            await foreach (var message in _messageChannel.Reader.ReadAllAsync(cancellationToken))
            {
                batch.Add(message);

                // Write batch when:
                // 1. Batch size reached
                // 2. Or timeout elapsed (checked via TryRead with timeout)
                if (batch.Count >= _batchSize)
                {
                    await WriteBatchAsync(batch, cancellationToken);
                    batch.Clear();
                }
                else
                {
                    // Try to collect more messages with timeout
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(_batchTimeout);

                    try
                    {
                        while (batch.Count < _batchSize && 
                               _messageChannel.Reader.TryRead(out var nextMessage))
                        {
                            batch.Add(nextMessage);
                        }
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // Timeout elapsed, write current batch
                    }

                    if (batch.Count > 0)
                    {
                        await WriteBatchAsync(batch, cancellationToken);
                        batch.Clear();
                    }
                }
            }

            // Write remaining messages
            if (batch.Count > 0)
            {
                await WriteBatchAsync(batch, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MessageWriter: Fatal error in writer loop: {ex}");
        }
    }

    private async Task WriteBatchAsync(List<RawMessage> batch, CancellationToken cancellationToken)
    {
        try
        {
            var messages = batch.Select(m => (m.SessionId, m.Timestamp, m.Direction, m.RawData));
            await _database.InsertMessageBatchAsync(messages, cancellationToken);

            Interlocked.Add(ref _totalMessagesWritten, batch.Count);
            Interlocked.Increment(ref _totalBatchesWritten);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MessageWriter: Error writing batch of {batch.Count} messages: {ex.Message}");
            // Could implement retry logic or dead-letter queue here
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _messageChannel.Writer.Complete();
        
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout during shutdown
        }

        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _messageChannel.Writer.Complete();

        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout during shutdown
        }

        _cts.Dispose();
    }
}

/// <summary>
/// Raw message for batch writing.
/// </summary>
public sealed class RawMessage
{
    public required string SessionId { get; init; }
    public required long Timestamp { get; init; }
    public required string Direction { get; init; }
    public required byte[] RawData { get; init; }
}

/// <summary>
/// Writer statistics for monitoring performance.
/// </summary>
public sealed class WriterStatistics
{
    public long TotalMessagesWritten { get; init; }
    public long TotalBatchesWritten { get; init; }
    public int QueuedMessages { get; init; }
    public int ChannelCapacity { get; init; }

    public double AverageBatchSize => TotalBatchesWritten > 0 
        ? TotalMessagesWritten / (double)TotalBatchesWritten 
        : 0;

    public double QueueUtilization => (QueuedMessages / (double)ChannelCapacity) * 100;
}
