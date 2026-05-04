using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;
using LogLevel = ComCross.Shared.Models.LogLevel;

namespace ComCross.Core.Services;

/// <summary>
/// Consumes frames from <see cref="IFrameStore"/> and appends user-visible messages into <see cref="IMessageStreamService"/>.
/// Supports pause/resume (consumption cursor per session).
/// </summary>
public sealed class FrameStoreMessageStreamPumpService : IDisposable
{
    private const int MaxLogBytes = 4 * 1024;
    private const int MaxFramesPerSessionPerPump = 256;

    private readonly IFrameStore _frameStore;
    private readonly IMessageFrameQueryService _queryService;
    private readonly IMessageStreamService _messageStream;
    private readonly ILogger<FrameStoreMessageStreamPumpService> _logger;

    private readonly Channel<string> _wake = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _cursors = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private int _started;

    public FrameStoreMessageStreamPumpService(
        IFrameStore frameStore,
        IMessageFrameQueryService queryService,
        IMessageStreamService messageStream,
        ILogger<FrameStoreMessageStreamPumpService> logger)
    {
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _frameStore.FramesAppended += OnFramesAppended;
        _messageStream.ConsumptionResumed += OnConsumptionResumed;
        Start();
    }

    private void OnFramesAppended(string sessionId) => Enqueue(sessionId);

    private void OnConsumptionResumed(string sessionId) => Enqueue(sessionId);

    private void Enqueue(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_pending.TryAdd(sessionId, 0))
        {
            _wake.Writer.TryWrite(sessionId);
        }
    }

    private void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _loop = Task.Run(LoopAsync);
        _logger.LogInformation("FrameStoreMessageStreamPumpService started.");
    }

    private async Task LoopAsync()
    {
        try
        {
            await foreach (var sessionId in _wake.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    if (_messageStream.IsConsumptionPaused(sessionId))
                    {
                        // Keep pending so resume can trigger.
                        continue;
                    }

                    await PumpSessionAsync(sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Pump session failed: {SessionId}", sessionId);
                }
                finally
                {
                    _pending.TryRemove(sessionId, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
    }

    private Task PumpSessionAsync(string sessionId)
    {
        var cursor = _cursors.GetOrAdd(sessionId, 0);
        while (true)
        {
            if (_messageStream.IsConsumptionPaused(sessionId))
            {
                return Task.CompletedTask;
            }

            var result = _queryService.Query(new MessageFrameQuery(
                sessionId,
                MessageFrameDataSource.LiveSpool,
                MessageFrameQueryKind.After,
                cursor,
                MaxFramesPerSessionPerPump));
            var firstAvailable = result.FirstAvailableFrameId ?? 0;
            if (result.Status == MessageFrameQueryStatus.DataEvicted && cursor + 1 < firstAvailable)
            {
                AppendSkippedMessage(sessionId, firstAvailable - (cursor + 1));
                cursor = firstAvailable - 1;
                _cursors[sessionId] = cursor;
            }

            if (result.Frames.Count == 0)
            {
                if (result.Status is MessageFrameQueryStatus.SourceUnavailable or MessageFrameQueryStatus.InvalidQuery)
                {
                    _logger.LogDebug("Message frame query returned {Status}: {SessionId}", result.Status, sessionId);
                }

                return Task.CompletedTask;
            }

            foreach (var frame in result.Frames)
            {
                cursor = frame.FrameId;
                _cursors[sessionId] = cursor;

                var content = FormatContent(frame);
                _messageStream.Append(sessionId, new LogMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Timestamp = frame.TimestampUtc,
                    Content = content,
                    Level = LogLevel.Info,
                    Source = frame.Direction == FrameDirection.Tx ? "TX" : "RX",
                    RawData = frame.RawData,
                    Format = frame.Format,
                    Attributes = frame.Attributes,
                    AttributeSchemaVersion = frame.AttributeSchemaVersion
                });
            }
        }
    }

    private void AppendSkippedMessage(string sessionId, long skipped)
    {
        _messageStream.Append(sessionId, new LogMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Content = $"[system] message consumption skipped {skipped} frames (window eviction)",
            Level = LogLevel.Warning,
            Source = "system",
            RawData = Array.Empty<byte>(),
            Format = MessageFormat.Text,
            Attributes = MessageFrameAttributes.Empty,
        });
    }

    private static string FormatContent(MessageFrameRecord frame)
    {
        var data = frame.RawData ?? Array.Empty<byte>();
        if (data.Length == 0)
        {
            return string.Empty;
        }

        if (frame.Format != MessageFormat.Hex)
        {
            try
            {
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                // fall back to hex
            }
        }

        var take = data.Length > MaxLogBytes ? MaxLogBytes : data.Length;
        var slice = take == data.Length ? data : data.AsSpan(0, take).ToArray();
        var hex = BitConverter.ToString(slice).Replace("-", " ");

        return take == data.Length
            ? hex
            : $"{hex} ... (+{data.Length - take} bytes)";
    }

    public void Dispose()
    {
        _frameStore.FramesAppended -= OnFramesAppended;
        _messageStream.ConsumptionResumed -= OnConsumptionResumed;

        try { _cts.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
