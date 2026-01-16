using ComCross.PluginSdk;
using Microsoft.Extensions.Logging;

namespace ComCross.Shared.Services;

/// <summary>
/// 共享内存全局管理器
/// 负责整个应用的共享内存资源管理、动态扩容、背压监控
/// </summary>
public class SharedMemoryManager : IDisposable
{
    private readonly ILogger<SharedMemoryManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SharedMemoryConfig _config;
    
    private SegmentedSharedMemory? _sharedMemory;
    private readonly Dictionary<string, SessionMemoryInfo> _sessionInfos = new();
    private readonly CancellationTokenSource _cancellationSource = new();
    private Task? _monitorTask;
    private bool _disposed;
    
    public SharedMemoryManager(
        SharedMemoryConfig config,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SharedMemoryManager>();
    }
    
    /// <summary>
    /// 初始化共享内存
    /// </summary>
    public void Initialize()
    {
        if (_sharedMemory != null)
            throw new InvalidOperationException("SharedMemoryManager已初始化");
        
        _sharedMemory = new SegmentedSharedMemory(
            _config.Name,
            _config.MaxTotalMemory,
            _loggerFactory.CreateLogger<SegmentedSharedMemory>());
        
        // 启动监控线程
        _monitorTask = Task.Run(MonitorSegmentsAsync);
        
        _logger.LogInformation(
            "SharedMemoryManager已初始化：MaxTotal={MaxTotal}MB",
            _config.MaxTotalMemory / (1024 * 1024));
    }
    
    /// <summary>
    /// 为Session分配Segment
    /// </summary>
    public async Task<SessionSegment> AllocateSegmentAsync(
        string sessionId,
        int requestedSize)
    {
        if (_sharedMemory == null)
            throw new InvalidOperationException("SharedMemoryManager未初始化");
        
        // 检查是否超过总限制，可能降级
        var stats = _sharedMemory.GetUsageStats();
        long availableSize = _config.MaxTotalMemory - stats.AllocatedSize;
        
        int actualSize = requestedSize;
        if (requestedSize > availableSize)
        {
            // 降级到最小Size
            actualSize = Math.Min(requestedSize, _config.MinSegmentSize);
            
            _logger.LogWarning(
                "内存不足，降级分配：SessionId={SessionId}, 请求{RequestedSize}KB, 实际{ActualSize}KB",
                sessionId, requestedSize / 1024, actualSize / 1024);
        }
        
        // 分配Segment
        var segmentLogger = _loggerFactory.CreateLogger<SessionSegment>();
        var segment = _sharedMemory.AllocateSegment(sessionId, actualSize, segmentLogger);
        
        // 记录Session信息
        _sessionInfos[sessionId] = new SessionMemoryInfo
        {
            SessionId = sessionId,
            AllocatedSize = actualSize,
            LastActivityTime = DateTime.UtcNow
        };
        
        return segment;
    }
    
    /// <summary>
    /// 释放Session的Segment
    /// </summary>
    public void ReleaseSegment(string sessionId)
    {
        _sharedMemory?.ReleaseSegment(sessionId);
        _sessionInfos.Remove(sessionId);
        
        _logger.LogInformation("已释放SessionSegment：{SessionId}", sessionId);
    }
    
    /// <summary>
    /// 获取Session的Segment
    /// </summary>
    public SessionSegment? GetSegment(string sessionId)
    {
        return _sharedMemory?.GetSegment(sessionId);
    }
    
    /// <summary>
    /// 监控所有Segment，处理背压和扩容
    /// </summary>
    private async Task MonitorSegmentsAsync()
    {
        _logger.LogInformation("共享内存监控线程已启动");
        
        while (!_cancellationSource.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cancellationSource.Token); // 每秒检查
                
                if (_sharedMemory == null)
                    continue;
                
                var sessionIds = _sharedMemory.GetSessionIds();
                
                foreach (var sessionId in sessionIds)
                {
                    var segment = _sharedMemory.GetSegment(sessionId);
                    if (segment == null)
                        continue;
                    
                    double usageRatio = segment.GetUsageRatio();
                    
                    // 高水位警告（80%）
                    if (usageRatio > _config.WarningThreshold)
                    {
                        _logger.LogWarning(
                            "⚠️ Session内存高水位：{SessionId}, 使用率{UsageRatio:P0}",
                            sessionId, usageRatio);
                        
                        // TODO: 触发背压通知（通知插件降速）
                        OnBackpressureDetected(sessionId, BackpressureLevel.High);
                    }
                    else if (usageRatio > 0.6)
                    {
                        OnBackpressureDetected(sessionId, BackpressureLevel.Medium);
                    }
                    
                    // 临界水位（95%）
                    if (usageRatio > _config.CriticalThreshold)
                    {
                        _logger.LogError(
                            "🔴 Session内存临界：{SessionId}, 使用率{UsageRatio:P0}，数据可能丢失！",
                            sessionId, usageRatio);
                    }
                }
                
                // 全局内存统计
                var stats = _sharedMemory.GetUsageStats();
                if (stats.AllocationRatio > 0.9)
                {
                    _logger.LogWarning(
                        "⚠️ 全局内存分配高：{AllocatedSize}MB / {TotalSize}MB ({Ratio:P0})",
                        stats.AllocatedSize / (1024 * 1024),
                        stats.TotalSize / (1024 * 1024),
                        stats.AllocationRatio);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "监控线程异常");
            }
        }
        
        _logger.LogInformation("共享内存监控线程已停止");
    }
    
    /// <summary>
    /// 背压检测事件
    /// </summary>
    private void OnBackpressureDetected(string sessionId, BackpressureLevel level)
    {
        // TODO: 通过IPC通知插件降速
        // 可以通过PluginHostClient发送背压通知
    }
    
    /// <summary>
    /// 获取全局统计信息
    /// </summary>
    public MemoryUsageStats? GetGlobalStats()
    {
        return _sharedMemory?.GetUsageStats();
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _cancellationSource.Cancel();
            _monitorTask?.Wait(TimeSpan.FromSeconds(2));
            
            _sharedMemory?.Dispose();
            _cancellationSource.Dispose();
            
            _disposed = true;
            _logger.LogInformation("SharedMemoryManager已释放");
        }
    }
}

/// <summary>
/// 共享内存配置
/// </summary>
public class SharedMemoryConfig
{
    /// <summary>
    /// 共享内存名称
    /// </summary>
    public string Name { get; set; } = "ComCross_SharedMemory";
    
    /// <summary>
    /// 最大总内存（字节）
    /// 默认：100MB
    /// </summary>
    public long MaxTotalMemory { get; set; } = 100 * 1024 * 1024;
    
    /// <summary>
    /// 单Session默认大小（字节）
    /// 默认：2MB
    /// </summary>
    public int DefaultSegmentSize { get; set; } = 2 * 1024 * 1024;
    
    /// <summary>
    /// 单Session最小大小（字节）
    /// 默认：512KB
    /// </summary>
    public int MinSegmentSize { get; set; } = 512 * 1024;
    
    /// <summary>
    /// 警告阈值（0.0 - 1.0）
    /// 默认：0.8（80%）
    /// </summary>
    public double WarningThreshold { get; set; } = 0.8;
    
    /// <summary>
    /// 临界阈值（0.0 - 1.0）
    /// 默认：0.95（95%）
    /// </summary>
    public double CriticalThreshold { get; set; } = 0.95;
}

/// <summary>
/// Session内存信息
/// </summary>
public class SessionMemoryInfo
{
    public required string SessionId { get; set; }
    public int AllocatedSize { get; set; }
    public DateTime LastActivityTime { get; set; }
}

/// <summary>
/// 背压等级
/// </summary>
public enum BackpressureLevel
{
    None,
    Medium,
    High
}
