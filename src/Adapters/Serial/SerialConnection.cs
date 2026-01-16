using System.IO.Ports;
using ComCross.PluginSdk;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Adapters.Serial;

/// <summary>
/// Serial port connection implementation with shared memory integration
/// </summary>
public sealed class SerialConnection : IDeviceConnection
{
    private SerialPort? _serialPort;
    private readonly string _port;
    private readonly ISerialPortAccessManager _accessManager;
    private readonly ILogger<SerialConnection>? _logger;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;
    
    // 共享内存集成
    private ISharedMemoryWriter? _sharedMemorySegment;
    private BackpressureLevel _backpressureLevel = BackpressureLevel.None;

    public string Port => _port;
    public bool IsOpen => _serialPort?.IsOpen ?? false;
    public SessionStatus Status { get; private set; } = SessionStatus.Disconnected;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;

    public SerialConnection(
        string port, 
        ISerialPortAccessManager accessManager,
        ILogger<SerialConnection>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(port);
        ArgumentNullException.ThrowIfNull(accessManager);
        
        _port = port;
        _accessManager = accessManager;
        _logger = logger;
    }
    
    /// <summary>
    /// 设置共享内存Segment（用于数据写入）
    /// </summary>
    public void SetSharedMemorySegment(ISharedMemoryWriter segment)
    {
        _sharedMemorySegment = segment;
        _logger?.LogInformation("[{Port}] 共享内存已设置", _port);
    }
    
    /// <summary>
    /// 设置背压等级（由主进程通知）
    /// </summary>
    public void SetBackpressureLevel(BackpressureLevel level)
    {
        _backpressureLevel = level;
        _logger?.LogDebug("[{Port}] 背压等级已更新：{Level}", _port, level);
    }

    public async Task OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_serialPort != null)
        {
            throw new InvalidOperationException("Connection already open");
        }

        // Check if we have permission to access the port
        var hasPermission = await _accessManager.HasAccessPermissionAsync(_port, cancellationToken);
        if (!hasPermission)
        {
            // Try to request permission automatically
            var result = await _accessManager.RequestAccessPermissionAsync(_port, cancellationToken);
            
            if (result != PermissionRequestResult.AlreadyGranted && result != PermissionRequestResult.Granted)
            {
                // Permission request failed
                throw new SerialPortAccessDeniedException(_port, 
                    $"Permission denied to access serial port {_port}. " +
                    $"Automatic permission request failed. Please grant permission manually.");
            }
            
            // Verify permission was granted
            hasPermission = await _accessManager.HasAccessPermissionAsync(_port, cancellationToken);
            if (!hasPermission)
            {
                throw new SerialPortAccessDeniedException(_port, 
                    $"Permission denied to access serial port {_port}. " +
                    $"Permission request appeared to succeed but access still denied.");
            }
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
        }
        catch (UnauthorizedAccessException ex) when (ex is not SerialPortAccessDeniedException)
        {
            Status = SessionStatus.Error;
            // Wrap as SerialPortAccessDeniedException for consistent handling
            throw new SerialPortAccessDeniedException(_port, 
                $"Access denied to serial port {_port}.", ex);
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
        Status = SessionStatus.Disconnected;
        
        // Cancel read task first
        if (_readCancellation != null)
        {
            try
            {
                await _readCancellation.CancelAsync();
            }
            catch { }
            
            _readCancellation?.Dispose();
            _readCancellation = null;
        }

        // Wait for read task with timeout
        if (_readTask != null)
        {
            try
            {
                await Task.WhenAny(_readTask, Task.Delay(TimeSpan.FromSeconds(3)));
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch { }
            _readTask = null;
        }

        // Force close serial port
        if (_serialPort != null)
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    _serialPort.Close();
                }
            }
            catch { }
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
        // 根据背压等级调整缓冲区大小
        int GetBufferSize() => _backpressureLevel switch
        {
            BackpressureLevel.High => 512,
            BackpressureLevel.Medium => 2048,
            _ => 4096
        };
        
        var buffer = new byte[GetBufferSize()];

        while (!cancellationToken.IsCancellationRequested && _serialPort?.IsOpen == true)
        {
            try
            {
                // 根据背压动态调整缓冲区
                if (buffer.Length != GetBufferSize())
                {
                    buffer = new byte[GetBufferSize()];
                }
                
                var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead > 0)
                {
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    
                    // 优先写入共享内存（如果已配置）
                    if (_sharedMemorySegment != null)
                    {
                        await WriteToSharedMemoryAsync(data, cancellationToken);
                    }
                    else
                    {
                        // 兼容旧模式：触发DataReceived事件
                        DataReceived?.Invoke(this, data);
                    }
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
    
    /// <summary>
    /// 写入数据到共享内存（带重试和背压处理）
    /// </summary>
    private async Task WriteToSharedMemoryAsync(byte[] data, CancellationToken cancellationToken)
    {
        int retryCount = 0;
        const int maxRetries = 10;
        
        while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
        {
            if (_sharedMemorySegment!.TryWriteFrame(data, out long frameId))
            {
                _logger?.LogTrace("[{Port}] 写入共享内存成功：FrameId={FrameId}, Size={Size}字节", 
                    _port, frameId, data.Length);
                return;
            }
            
            // 写入失败，等待主进程消费
            retryCount++;
            _logger?.LogDebug("[{Port}] 共享内存满，等待主进程消费（重试{Retry}/{Max}）",
                _port, retryCount, maxRetries);
            
            await Task.Delay(10 * retryCount, cancellationToken); // 指数退避
        }
        
        // 超过重试次数，丢弃数据
        _logger?.LogWarning("[{Port}] ⚠️ 共享内存持续满，丢弃{Size}字节数据", _port, data.Length);
    }

    public void Dispose()
    {
        try
        {
            // Try async close with timeout
            var closeTask = CloseAsync();
            if (!closeTask.Wait(TimeSpan.FromSeconds(2)))
            {
                // Force close if timeout
                _readCancellation?.Dispose();
                _serialPort?.Close();
            }
        }
        catch
        {
            // Force close on error
            try
            {
                _serialPort?.Close();
            }
            catch { }
        }
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
