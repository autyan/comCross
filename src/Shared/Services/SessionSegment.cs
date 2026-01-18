using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using ComCross.PluginSdk;
using Microsoft.Extensions.Logging;

namespace ComCross.Shared.Services;

/// <summary>
/// Session专用共享内存段
/// 提供边界检查和环形缓冲区功能，防止插件写越界
/// 实现ISharedMemoryWriter接口供插件安全使用
/// </summary>
public sealed class SessionSegment : ISharedMemoryWriter, IDisposable
{
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly ILogger<SessionSegment>? _logger;
    private readonly string _sessionId;
    private readonly int _segmentSize;
    private readonly int _dataOffset;
    private readonly int _dataSize;

    private bool _disposed;
    
    // SessionHeader布局（256字节）
    private const int HEADER_SIZE = 256;
    private const int OFFSET_SESSION_ID = 0;      // 0-127: SessionId (128 bytes)
    private const int OFFSET_WRITE_POS = 128;     // 128-135: WritePosition (8 bytes)
    private const int OFFSET_READ_POS = 136;      // 136-143: ReadPosition (8 bytes)
    private const int OFFSET_FRAME_SEQ = 144;     // 144-151: FrameIdSequence (8 bytes)
    private const int OFFSET_SEGMENT_SIZE = 152;  // 152-155: SegmentSize (4 bytes)
    
    public SessionSegment(
        string sessionId,
        MemoryMappedViewAccessor accessor,
        int segmentSize,
        ILogger<SessionSegment>? logger = null,
        bool initializeHeader = true)
    {
        _sessionId = sessionId;
        _accessor = accessor;
        _segmentSize = segmentSize;
        _logger = logger;
        
        _dataOffset = HEADER_SIZE;
        _dataSize = segmentSize - HEADER_SIZE;
        
        if (initializeHeader)
        {
            WriteHeader();
        }
    }
    
    /// <summary>
    /// 尝试写入物理帧（带边界检查）
    /// 🔒 核心安全机制：检查可用空间，防止写越界
    /// </summary>
    public bool TryWriteFrame(ReadOnlySpan<byte> data, out long frameId)
    {
        frameId = -1;
        
        if (_disposed)
        {
            _logger?.LogWarning("[{SessionId}] SessionSegment已释放，拒绝写入", _sessionId);
            return false;
        }
        
        // 帧格式：[Length:4字节][Data:N字节]
        int frameSize = 4 + data.Length;
        
        // ✅ 边界检查1：检查单帧大小是否超过总容量
        if (frameSize > _dataSize)
        {
            _logger?.LogError(
                "[{SessionId}] 单帧过大：{FrameSize}字节 > 容量{DataSize}字节，拒绝写入",
                _sessionId, frameSize, _dataSize);
            return false;
        }
        
        // Refresh positions from shared header (cross-process safe).
        long writePosition = ReadWritePosition();
        long readPosition = ReadReadPosition();

        // ✅ 边界检查2：检查环形缓冲区是否有足够空间
        long used = writePosition - readPosition;
        long freeSpace = _dataSize - used;
        if (frameSize > freeSpace)
        {
            _logger?.LogDebug(
                "[{SessionId}] 空间不足：需要{FrameSize}字节，剩余{FreeSpace}字节",
                _sessionId, frameSize, freeSpace);
            return false;
        }

        // 分配FrameId（single-writer assumption; cross-process atomicity is out of scope for MVP）
        var seq = ReadFrameSequence();
        seq++;
        WriteFrameSequence(seq);
        frameId = seq;

        // 写入Length（4字节）
        int logicalWritePos = (int)(writePosition % _dataSize);
        WriteInt32Wrapped(logicalWritePos, data.Length);
        
        // 写入Data（处理环绕）
        int dataWritePos = (int)((writePosition + 4) % _dataSize);
        WriteDataWrapped(dataWritePos, data);
        
        // 更新WritePosition
        writePosition += frameSize;
        WriteWritePosition(writePosition);
        
        _logger?.LogTrace(
            "[{SessionId}] 写入帧#{FrameId}，大小{Size}字节，WritePos={WritePos}",
            _sessionId, frameId, data.Length, writePosition);
        
        return true;
    }
    
    /// <summary>
    /// 尝试读取一帧数据
    /// </summary>
    public bool TryReadFrame(out byte[] data)
    {
        data = Array.Empty<byte>();
        
        if (_disposed)
            return false;
        
        long writePosition = ReadWritePosition();
        long readPosition = ReadReadPosition();

        // 检查是否有数据可读
        if (readPosition >= writePosition)
            return false;
        
        // 读取Length
        int logicalReadPos = (int)(readPosition % _dataSize);
        int dataLength = ReadInt32Wrapped(logicalReadPos);
        
        // 验证长度合法性
        if (dataLength <= 0 || dataLength > _dataSize)
        {
            _logger?.LogError(
                "[{SessionId}] 读取到非法长度：{Length}，跳过此帧",
                _sessionId, dataLength);
            return false;
        }
        
        // 读取Data
        int dataReadPos = (int)((readPosition + 4) % _dataSize);
        data = ReadDataWrapped(dataReadPos, dataLength);
        
        // 更新ReadPosition
        readPosition += 4 + dataLength;
        WriteReadPosition(readPosition);
        
        _logger?.LogTrace(
            "[{SessionId}] 读取帧，大小{Size}字节，ReadPos={ReadPos}",
            _sessionId, dataLength, readPosition);
        
        return true;
    }
    
    /// <summary>
    /// 获取可用空间（字节）
    /// </summary>
    public long GetFreeSpace()
    {
        long writePosition = ReadWritePosition();
        long readPosition = ReadReadPosition();
        long used = writePosition - readPosition;
        return _dataSize - used;
    }
    
    /// <summary>
    /// 获取已使用空间（字节）
    /// </summary>
    public long GetUsedSpace()
    {
        long writePosition = ReadWritePosition();
        long readPosition = ReadReadPosition();
        return writePosition - readPosition;
    }
    
    /// <summary>
    /// 获取使用率（0.0 - 1.0）
    /// </summary>
    public double GetUsageRatio()
    {
        return (double)GetUsedSpace() / _dataSize;
    }
    
    /// <summary>
    /// 写入Header（初始化或更新）
    /// </summary>
    private void WriteHeader()
    {
        // SessionId（最多127字节，UTF-8编码）
        byte[] sessionIdBytes = System.Text.Encoding.UTF8.GetBytes(_sessionId);
        int copyLength = Math.Min(sessionIdBytes.Length, 127);
        _accessor.WriteArray(OFFSET_SESSION_ID, sessionIdBytes, 0, copyLength);
        _accessor.Write(OFFSET_SESSION_ID + copyLength, (byte)0); // null-terminated
        
        // WritePosition, ReadPosition, FrameIdSequence
        _accessor.Write(OFFSET_WRITE_POS, 0L);
        _accessor.Write(OFFSET_READ_POS, 0L);
        _accessor.Write(OFFSET_FRAME_SEQ, 0L);
        
        // SegmentSize
        _accessor.Write(OFFSET_SEGMENT_SIZE, _segmentSize);
    }

    private long ReadWritePosition() => _accessor.ReadInt64(OFFSET_WRITE_POS);

    private long ReadReadPosition() => _accessor.ReadInt64(OFFSET_READ_POS);

    private long ReadFrameSequence() => _accessor.ReadInt64(OFFSET_FRAME_SEQ);

    private void WriteWritePosition(long value) => _accessor.Write(OFFSET_WRITE_POS, value);

    private void WriteReadPosition(long value) => _accessor.Write(OFFSET_READ_POS, value);

    private void WriteFrameSequence(long value) => _accessor.Write(OFFSET_FRAME_SEQ, value);
    
    /// <summary>
    /// 写入Int32（处理环绕）
    /// </summary>
    private void WriteInt32Wrapped(int position, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        
        for (int i = 0; i < 4; i++)
        {
            int physicalPos = _dataOffset + ((position + i) % _dataSize);
            _accessor.Write(physicalPos, bytes[i]);
        }
    }
    
    /// <summary>
    /// 读取Int32（处理环绕）
    /// </summary>
    private int ReadInt32Wrapped(int position)
    {
        Span<byte> bytes = stackalloc byte[4];
        
        for (int i = 0; i < 4; i++)
        {
            int physicalPos = _dataOffset + ((position + i) % _dataSize);
            bytes[i] = _accessor.ReadByte(physicalPos);
        }
        
        return BitConverter.ToInt32(bytes);
    }
    
    /// <summary>
    /// 写入数据（处理环绕）
    /// </summary>
    private void WriteDataWrapped(int position, ReadOnlySpan<byte> data)
    {
        int remaining = data.Length;
        int sourceOffset = 0;
        
        while (remaining > 0)
        {
            int physicalPos = _dataOffset + ((position + sourceOffset) % _dataSize);
            int chunkSize = Math.Min(remaining, _dataSize - (physicalPos - _dataOffset));
            
            _accessor.WriteArray(physicalPos, data.Slice(sourceOffset, chunkSize).ToArray(), 0, chunkSize);
            
            sourceOffset += chunkSize;
            remaining -= chunkSize;
        }
    }
    
    /// <summary>
    /// 读取数据（处理环绕）
    /// </summary>
    private byte[] ReadDataWrapped(int position, int length)
    {
        byte[] result = new byte[length];
        int remaining = length;
        int destOffset = 0;
        
        while (remaining > 0)
        {
            int physicalPos = _dataOffset + ((position + destOffset) % _dataSize);
            int chunkSize = Math.Min(remaining, _dataSize - (physicalPos - _dataOffset));
            
            _accessor.ReadArray(physicalPos, result, destOffset, chunkSize);
            
            destOffset += chunkSize;
            remaining -= chunkSize;
        }
        
        return result;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _accessor.Dispose();
            _disposed = true;
            
            _logger?.LogDebug("[{SessionId}] SessionSegment已释放", _sessionId);
        }
    }
}
