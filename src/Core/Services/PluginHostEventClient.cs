using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class PluginHostEventClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PluginHostEventClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public event Action<PluginHostEvent>? EventReceived;
    public event Action<Exception>? Faulted;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
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

        try
        {
            _reader?.Dispose();
        }
        catch
        {
        }

        _reader = null;

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
        }

        _pipe = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(cancellationToken);
            _reader = new StreamReader(_pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                PluginHostEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<PluginHostEvent>(line, _jsonOptions);
                }
                catch
                {
                    continue;
                }

                if (evt is null)
                {
                    continue;
                }

                try
                {
                    EventReceived?.Invoke(evt);
                }
                catch
                {
                    // Event handlers must not crash the loop.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        catch (Exception ex)
        {
            try
            {
                Faulted?.Invoke(ex);
            }
            catch
            {
            }
        }
    }
}
