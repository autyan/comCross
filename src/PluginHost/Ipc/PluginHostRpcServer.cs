using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Models;
using ComCross.PluginHost.Logging;

namespace ComCross.PluginHost.Ipc;

internal sealed class PluginHostRpcServer
{
    private readonly string _pipeName;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly PluginHostLogService _log;

    public PluginHostRpcServer(string pipeName, JsonSerializerOptions jsonOptions, PluginHostLogService log)
    {
        _pipeName = pipeName;
        _jsonOptions = jsonOptions;
        _log = log;
    }

    public async Task RunAsync(Func<PluginHostRequest, CancellationToken, Task<PluginHostResponse>> handler, CancellationToken cancellationToken = default)
    {
        await using var server = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            await server.WaitForConnectionAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            PluginHostRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<PluginHostRequest>(line, _jsonOptions);
            }
            catch (Exception ex)
            {
                _log.Warn($"Invalid request JSON: {ex.Message}");
                var response = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, ex.Message);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, _jsonOptions));
                continue;
            }

            if (request is null)
            {
                var response = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, "Invalid request.");
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, _jsonOptions));
                continue;
            }

            PluginHostResponse responseMessage;
            try
            {
                responseMessage = await handler(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _log.Error($"Unhandled handler error: type={request.Type}, id={request.Id}, err={ex.Message}");
                responseMessage = new PluginHostResponse(request.Id, false, ex.Message);
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(responseMessage, _jsonOptions));

            if (request.Type == PluginHostMessageTypes.Shutdown)
            {
                break;
            }
        }
    }
}
