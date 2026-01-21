using System.Threading.Channels;
using ComCross.PluginSdk;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// 共享内存读取器
/// 从插件进程的共享内存中读取物理帧，转换为LogMessage
/// </summary>
public class SharedMemoryReader : IDisposable
{
    private readonly ILogger<SharedMemoryReader> _logger;
    private readonly Channel<PhysicalFrame> _frameChannel;
    private readonly CancellationTokenSource _cancellationSource = new();
    private readonly Dictionary<string, (Task Task, CancellationTokenSource Cts)> _readerTasks = new();
    private readonly object _gate = new();
    private bool _disposed;
    
    public SharedMemoryReader(ILogger<SharedMemoryReader> logger)
    {
        _logger = logger;

        // Use a bounded channel to avoid unbounded memory growth if UI is slow.
        _frameChannel = Channel.CreateBounded<PhysicalFrame>(new BoundedChannelOptions(2048)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }
    
    /// <summary>
    /// 启动Session的读取任务
    /// </summary>
    public void StartReading(string sessionId, SessionSegment segment)
    {
        lock (_gate)
        {
            if (_readerTasks.ContainsKey(sessionId))
            {
                _logger.LogWarning("Session {SessionId} 读取任务已存在", sessionId);
                return;
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancellationSource.Token);
            var task = Task.Run(() => ReadLoopAsync(sessionId, segment, linked.Token));
            _readerTasks[sessionId] = (task, linked);

            _logger.LogInformation("Session {SessionId} 读取任务已启动", sessionId);
        }
    }
    
    /// <summary>
    /// 停止Session的读取任务
    /// </summary>
    public async Task StopReadingAsync(string sessionId)
    {
        (Task Task, CancellationTokenSource Cts)? entry = null;

        lock (_gate)
        {
            if (_readerTasks.TryGetValue(sessionId, out var existing))
            {
                entry = existing;
                _readerTasks.Remove(sessionId);
            }
        }

        if (entry is null)
        {
            return;
        }

        try
        {
            entry.Value.Cts.Cancel();
        }
        catch
        {
        }

        try
        {
            await Task.WhenAny(entry.Value.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        catch
        {
        }

        try
        {
            entry.Value.Cts.Dispose();
        }
        catch
        {
        }

        _logger.LogInformation("Session {SessionId} 读取任务已停止", sessionId);
    }
    
    /// <summary>
    /// 获取物理帧Channel（供上层消费）
    /// </summary>
    public ChannelReader<PhysicalFrame> GetFrameReader()
    {
        return _frameChannel.Reader;
    }
    
    /// <summary>
    /// 读取循环（从共享内存读取物理帧）
    /// </summary>
    private async Task ReadLoopAsync(
        string sessionId, 
        SessionSegment segment,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("[{SessionId}] 开始从共享内存读取物理帧", sessionId);
        
        long frameIdSequence = 0;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 尝试读取一帧
                if (segment.TryReadFrame(out byte[] data))
                {
                    var frame = new PhysicalFrame
                    {
                        FrameId = ++frameIdSequence,
                        SessionId = sessionId,
                        Data = data,
                        Timestamp = DateTime.UtcNow,
                        Direction = MessageDirection.Receive // TODO: 从数据中解析方向
                    };
                    
                    // 写入Channel
                    // Best-effort: bounded channel may drop old frames.
                    _frameChannel.Writer.TryWrite(frame);
                    
                    _logger.LogTrace(
                        "[{SessionId}] 读取物理帧#{FrameId}，大小{Size}字节",
                        sessionId, frame.FrameId, data.Length);
                }
                else
                {
                    // 无数据可读，短暂等待
                    await Task.Delay(1, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{SessionId}] 读取共享内存异常", sessionId);
                await Task.Delay(100, cancellationToken);
            }
        }
        
        _logger.LogDebug("[{SessionId}] 共享内存读取循环已退出", sessionId);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _cancellationSource.Cancel();

            (Task Task, CancellationTokenSource Cts)[] tasks;
            lock (_gate)
            {
                tasks = _readerTasks.Values.ToArray();
                _readerTasks.Clear();
            }

            try
            {
                foreach (var t in tasks)
                {
                    try { t.Cts.Cancel(); } catch { }
                }

                Task.WaitAll(tasks.Select(t => t.Task).ToArray(), TimeSpan.FromSeconds(3));
            }
            catch
            {
            }
            finally
            {
                foreach (var t in tasks)
                {
                    try { t.Cts.Dispose(); } catch { }
                }
            }
            
            _frameChannel.Writer.Complete();
            _cancellationSource.Dispose();
            
            _disposed = true;
            _logger.LogInformation("SharedMemoryReader已释放");
        }
    }
}
