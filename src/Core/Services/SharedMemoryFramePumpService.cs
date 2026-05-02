using System.Text;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Consumes physical frames produced by <see cref="SharedMemoryReader"/>,
/// then publishes system events and appends user-visible RX log messages.
///
/// This completes the RX pipeline:
/// SharedMemory -> PhysicalFrame -> DataReceivedEvent + LogMessage stream.
/// </summary>
public sealed class SharedMemoryFramePumpService : IDisposable
{
    private const int MaxLogBytes = 4 * 1024;

    private readonly SharedMemoryReader _reader;
    private readonly IEventBus _eventBus;
    private readonly IMessageStreamService _messageStream;
    private readonly ILogger<SharedMemoryFramePumpService> _logger;

    private readonly CancellationTokenSource _cts = new();
    private Task? _pumpTask;
    private int _started;

    public SharedMemoryFramePumpService(
        SharedMemoryReader reader,
        IEventBus eventBus,
        IMessageStreamService messageStream,
        ILogger<SharedMemoryFramePumpService> logger)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Start();
    }

    private void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _pumpTask = Task.Run(PumpLoopAsync);
        _logger.LogInformation("SharedMemoryFramePump started.");
    }

    private async Task PumpLoopAsync()
    {
        try
        {
            var reader = _reader.GetFrameReader();
            await foreach (var frame in reader.ReadAllAsync(_cts.Token))
            {
                if (frame is null || string.IsNullOrWhiteSpace(frame.SessionId))
                {
                    continue;
                }

                try
                {
                    // 1) Publish bytes-received event (updates RX bytes via DeviceService subscription)
                    _eventBus.Publish(new DataReceivedEvent(frame.SessionId, frame.Data, frame.Data.Length));

                    // 2) Append a user-visible message
                    var content = FormatRxContent(frame);
                    _messageStream.Append(frame.SessionId, new LogMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Timestamp = frame.Timestamp,
                        Content = content,
                        Level = ComCross.Shared.Models.LogLevel.Info,
                        Source = "rx",
                        RawData = frame.Data,
                        Format = MessageFormat.Hex
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to process frame for SessionId={SessionId}", frame.SessionId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SharedMemoryFramePump crashed.");
        }
    }

    private static string FormatRxContent(PhysicalFrame frame)
    {
        var prefix = frame.Direction == MessageDirection.Send ? "TX" : "RX";

        var data = frame.Data ?? Array.Empty<byte>();
        if (data.Length == 0)
        {
            return $"{prefix}:";
        }

        var take = data.Length > MaxLogBytes ? MaxLogBytes : data.Length;
        var slice = take == data.Length ? data : data.AsSpan(0, take).ToArray();

        // Hex with spaces.
        var hex = BitConverter.ToString(slice).Replace("-", " ");

        if (take == data.Length)
        {
            return $"{prefix}: {hex}";
        }

        return $"{prefix}: {hex} ... (+{data.Length - take} bytes)";
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try
        {
            _pumpTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
