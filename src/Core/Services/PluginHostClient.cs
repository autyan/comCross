using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class PluginHostClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PluginHostClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<PluginHostResponse?> SendAsync(PluginHostRequest request, TimeSpan timeout)
    {
        try
        {
            await EnsureConnectedAsync(timeout).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (_writer is null || _reader is null)
        {
            return null;
        }

        var payload = JsonSerializer.Serialize(request, _jsonOptions);
        await _writer.WriteLineAsync(payload).ConfigureAwait(false);

        var line = await ReadLineAsync(_reader, timeout).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PluginHostResponse>(line, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _writer = null;
        _reader = null;
        _pipe = null;
    }

    private async Task EnsureConnectedAsync(TimeSpan timeout)
    {
        if (_pipe is { IsConnected: true })
        {
            return;
        }

        Dispose();
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(timeout);
        await _pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
        _reader = new StreamReader(_pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(_pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, TimeSpan timeout)
    {
        var readTask = reader.ReadLineAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
        return completed == readTask ? await readTask.ConfigureAwait(false) : null;
    }
}
