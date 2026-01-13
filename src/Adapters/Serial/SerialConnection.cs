using System.IO.Ports;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Adapters.Serial;

/// <summary>
/// Serial port connection implementation
/// </summary>
public sealed class SerialConnection : IDeviceConnection
{
    private SerialPort? _serialPort;
    private readonly string _port;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;

    public string Port => _port;
    public bool IsOpen => _serialPort?.IsOpen ?? false;
    public SessionStatus Status { get; private set; } = SessionStatus.Disconnected;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;

    public SerialConnection(string port)
    {
        ArgumentException.ThrowIfNullOrEmpty(port);
        _port = port;
    }

    public Task OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_serialPort != null)
        {
            throw new InvalidOperationException("Connection already open");
        }

        try
        {
            _serialPort = new SerialPort(_port)
            {
                BaudRate = settings.BaudRate,
                DataBits = settings.DataBits,
                Parity = MapParity(settings.Parity),
                StopBits = MapStopBits(settings.StopBits),
                Handshake = MapHandshake(settings.FlowControl),
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            _serialPort.Open();
            Status = SessionStatus.Connected;

            // Start reading in background
            _readCancellation = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_readCancellation.Token), _readCancellation.Token);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Status = SessionStatus.Error;
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public async Task CloseAsync()
    {
        if (_readCancellation != null)
        {
            await _readCancellation.CancelAsync();
            _readCancellation.Dispose();
            _readCancellation = null;
        }

        if (_readTask != null)
        {
            try
            {
                await _readTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            _readTask = null;
        }

        if (_serialPort != null)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort.Dispose();
            _serialPort = null;
        }

        Status = SessionStatus.Disconnected;
    }

    public Task<int> WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (_serialPort == null || !_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Port not open");
        }

        try
        {
            _serialPort.Write(data, 0, data.Length);
            return Task.FromResult(data.Length);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(int maxBytes = 1024, CancellationToken cancellationToken = default)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Port not open");
        }

        var buffer = new byte[maxBytes];
        var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, cancellationToken);

        var result = new byte[bytesRead];
        Array.Copy(buffer, result, bytesRead);
        return result;
    }

    private async void ReadLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested && _serialPort?.IsOpen == true)
        {
            try
            {
                var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead > 0)
                {
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    DataReceived?.Invoke(this, data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex.Message);
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }

    private static System.IO.Ports.Parity MapParity(Shared.Models.Parity parity) => parity switch
    {
        Shared.Models.Parity.None => System.IO.Ports.Parity.None,
        Shared.Models.Parity.Odd => System.IO.Ports.Parity.Odd,
        Shared.Models.Parity.Even => System.IO.Ports.Parity.Even,
        Shared.Models.Parity.Mark => System.IO.Ports.Parity.Mark,
        Shared.Models.Parity.Space => System.IO.Ports.Parity.Space,
        _ => System.IO.Ports.Parity.None
    };

    private static System.IO.Ports.StopBits MapStopBits(Shared.Models.StopBits stopBits) => stopBits switch
    {
        Shared.Models.StopBits.None => System.IO.Ports.StopBits.None,
        Shared.Models.StopBits.One => System.IO.Ports.StopBits.One,
        Shared.Models.StopBits.Two => System.IO.Ports.StopBits.Two,
        Shared.Models.StopBits.OnePointFive => System.IO.Ports.StopBits.OnePointFive,
        _ => System.IO.Ports.StopBits.One
    };

    private static System.IO.Ports.Handshake MapHandshake(Shared.Models.Handshake handshake) => handshake switch
    {
        Shared.Models.Handshake.None => System.IO.Ports.Handshake.None,
        Shared.Models.Handshake.XOnXOff => System.IO.Ports.Handshake.XOnXOff,
        Shared.Models.Handshake.RequestToSend => System.IO.Ports.Handshake.RequestToSend,
        Shared.Models.Handshake.RequestToSendXOnXOff => System.IO.Ports.Handshake.RequestToSendXOnXOff,
        _ => System.IO.Ports.Handshake.None
    };
}
