using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Events;

internal sealed class HostEventSink : IDisposable
{
    private readonly string? _pipeName;
    private readonly Channel<string> _queue;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public HostEventSink(string? pipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? null : pipeName;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        if (_pipeName is not null)
        {
            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptAndWriteLoopAsync(_cts.Token));
        }
    }

    public void PublishUiStateInvalidated(PluginUiStateInvalidatedEvent evt)
    {
        if (_pipeName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(evt.CapabilityId))
        {
            return;
        }

        // ADR-010 closure: sessionless is represented by null only.
        if (evt.SessionId is not null && string.IsNullOrWhiteSpace(evt.SessionId))
        {
            return;
        }

        try
        {
            var hostPayload = new PluginHostUiStateInvalidatedEvent(
                CapabilityId: evt.CapabilityId,
                SessionId: evt.SessionId,
                ViewKind: evt.ViewKind,
                ViewInstanceId: evt.ViewInstanceId,
                Reason: evt.Reason);

            var payloadJson = JsonSerializer.Serialize(hostPayload, _jsonOptions);
            var payload = JsonDocument.Parse(payloadJson).RootElement.Clone();
            var envelope = new PluginHostEvent(PluginHostEventTypes.UiStateInvalidated, payload);
            var line = JsonSerializer.Serialize(envelope, _jsonOptions);
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // best-effort
        }
    }

    public void PublishHostRegistered(string? token)
    {
        if (_pipeName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            var payload = new PluginHostRegisteredEvent(token, Process.GetCurrentProcess().Id);
            var payloadJson = JsonSerializer.Serialize(payload, _jsonOptions);
            var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
            var envelope = new PluginHostEvent(PluginHostEventTypes.HostRegistered, payloadElement);
            var line = JsonSerializer.Serialize(envelope, _jsonOptions);
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // best-effort
        }
    }

    public void PublishSessionRegistered(string token, string sessionId)
    {
        if (_pipeName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var payload = new PluginHostSessionRegisteredEvent(token, Process.GetCurrentProcess().Id, sessionId);
            var payloadJson = JsonSerializer.Serialize(payload, _jsonOptions);
            var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
            var envelope = new PluginHostEvent(PluginHostEventTypes.SessionRegistered, payloadElement);
            var line = JsonSerializer.Serialize(envelope, _jsonOptions);
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // best-effort
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        _cts = null;
        _acceptLoop = null;
    }

    private async Task AcceptAndWriteLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName!,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                await using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };

                while (!cancellationToken.IsCancellationRequested && await _queue.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (_queue.Reader.TryRead(out var line))
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // If client disconnects or pipe breaks, loop back and accept again.
                await Task.Delay(200, cancellationToken);
            }
        }
    }
}
