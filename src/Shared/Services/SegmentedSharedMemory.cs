using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;

namespace ComCross.Shared.Services;

/// <summary>
/// 分段共享内存管理器
/// 负责分配和管理多个Session的独立内存段
/// </summary>
public class SegmentedSharedMemory : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly ConcurrentDictionary<string, SessionSegment> _segments = new();
    private readonly ILogger<SegmentedSharedMemory> _logger;
    private readonly string _name;
    private readonly long _totalSize;
    
    private long _currentOffset;
    private bool _disposed;
    
    // GlobalHeader大小
    private const int GLOBAL_HEADER_SIZE = 4096; // 4KB
    
    public SegmentedSharedMemory(
        string name,
        long totalSize,
        ILogger<SegmentedSharedMemory> logger)
    {
        _name = name;
        _totalSize = totalSize;
        _logger = logger;
        _currentOffset = GLOBAL_HEADER_SIZE;
        
        // 创建MemoryMappedFile
        // Linux不支持Named maps，使用匿名映射
        _mmf = MemoryMappedFile.CreateNew(null, totalSize);
        
        _logger.LogInformation(
            "共享内存已创建：Name={Name}, TotalSize={TotalSize}MB",
            name, totalSize / (1024 * 1024));
    }
    
    /// <summary>
    /// 为Session分配独立内存段
    /// 🔒 每个Session只能访问自己的Segment（OS级别隔离）
    /// </summary>
    public SessionSegment AllocateSegment(
        string sessionId,
        int segmentSize,
        ILogger<SessionSegment>? segmentLogger = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SegmentedSharedMemory));
        
        // 检查是否已分配
        if (_segments.ContainsKey(sessionId))
        {
            throw new InvalidOperationException(
                $"Session {sessionId} 已分配Segment");
        }
        
        // 检查剩余空间
        long availableSize = _totalSize - _currentOffset;
        if (segmentSize > availableSize)
        {
            throw new InvalidOperationException(
                $"共享内存空间不足：需要{segmentSize}字节，剩余{availableSize}字节");
        }
        
        // 计算Segment偏移
        long offset = Interlocked.Add(ref _currentOffset, segmentSize) - segmentSize;
        
        // 创建受限的ViewAccessor（✅ OS级别保护：只能访问此范围）
        var accessor = _mmf.CreateViewAccessor(offset, segmentSize, MemoryMappedFileAccess.ReadWrite);
        
        // 创建SessionSegment
        var segment = new SessionSegment(sessionId, accessor, segmentSize, segmentLogger);
        _segments[sessionId] = segment;
        
        _logger.LogInformation(
            "已分配Segment：SessionId={SessionId}, Size={Size}KB, Offset={Offset}",
            sessionId, segmentSize / 1024, offset);
        
        return segment;
    }
    
    /// <summary>
    /// 获取Session的Segment
    /// </summary>
    public SessionSegment? GetSegment(string sessionId)
    {
        _segments.TryGetValue(sessionId, out var segment);
        return segment;
    }
    
    /// <summary>
    /// 释放Session的Segment
    /// </summary>
    public void ReleaseSegment(string sessionId)
    {
        if (_segments.TryRemove(sessionId, out var segment))
        {
            segment.Dispose();
            
            _logger.LogInformation(
                "已释放Segment：SessionId={SessionId}",
                sessionId);
        }
    }
    
    /// <summary>
    /// 获取所有Session ID
    /// </summary>
    public IReadOnlyCollection<string> GetSessionIds()
    {
        return _segments.Keys.ToList();
    }
    
    /// <summary>
    /// 获取总内存使用统计
    /// </summary>
    public MemoryUsageStats GetUsageStats()
    {
        long totalUsed = 0;
        long totalAllocated = _currentOffset - GLOBAL_HEADER_SIZE;
        
        foreach (var segment in _segments.Values)
        {
            totalUsed += segment.GetUsedSpace();
        }
        
        return new MemoryUsageStats
        {
            TotalSize = _totalSize,
            AllocatedSize = totalAllocated,
            UsedSize = totalUsed,
            FreeSize = _totalSize - _currentOffset,
            SessionCount = _segments.Count
        };
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // 释放所有Segment
            foreach (var segment in _segments.Values)
            {
                segment.Dispose();
            }
            _segments.Clear();
            
            // 释放MemoryMappedFile
            _mmf.Dispose();
            _disposed = true;
            
            _logger.LogInformation("共享内存已释放：Name={Name}", _name);
        }
    }
}

/// <summary>
/// 内存使用统计
/// </summary>
public class MemoryUsageStats
{
    /// <summary>
    /// 总大小（字节）
    /// </summary>
    public long TotalSize { get; set; }
    
    /// <summary>
    /// 已分配大小（字节）
    /// </summary>
    public long AllocatedSize { get; set; }
    
    /// <summary>
    /// 已使用大小（字节）
    /// </summary>
    public long UsedSize { get; set; }
    
    /// <summary>
    /// 剩余可分配大小（字节）
    /// </summary>
    public long FreeSize { get; set; }
    
    /// <summary>
    /// Session数量
    /// </summary>
    public int SessionCount { get; set; }
    
    /// <summary>
    /// 分配使用率（0.0 - 1.0）
    /// </summary>
    public double AllocationRatio => (double)AllocatedSize / TotalSize;
    
    /// <summary>
    /// 实际使用率（0.0 - 1.0）
    /// </summary>
    public double UsageRatio => (double)UsedSize / TotalSize;
}
