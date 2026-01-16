using System.IO.MemoryMappedFiles;

namespace ComCross.Core.Services;

/// <summary>
/// Shared memory segment for single session data exchange
/// Uses ring buffer for efficient data streaming between plugin and main process
/// </summary>
public sealed class SharedMemorySegment : IDisposable
{
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly int _segmentSize;
    private readonly int _headerSize = 256;  // 256 bytes for metadata
    private readonly object _lock = new();
    
    // Header offsets
    private const int SessionIdOffset = 0;        // 36 bytes (GUID string)
    private const int WritePositionOffset = 40;   // 8 bytes (long)
    private const int ReadPositionOffset = 48;    // 8 bytes (long)
    private const int SegmentSizeOffset = 56;     // 4 bytes (int)
    private const int TotalFramesOffset = 60;     // 8 bytes (long)
    
    public string SessionId { get; }
    public int DataBufferSize => _segmentSize - _headerSize;
    
    public SharedMemorySegment(
        string sessionId,
        MemoryMappedViewAccessor accessor,
        int segmentSize)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _segmentSize = segmentSize;
        
        // Initialize header
        InitializeHeader();
    }
    
    private void InitializeHeader()
    {
        lock (_lock)
        {
            // Write session ID
            WriteString(SessionIdOffset, SessionId, 36);
            
            // Initialize positions
            _accessor.Write(WritePositionOffset, 0L);
            _accessor.Write(ReadPositionOffset, 0L);
            _accessor.Write(SegmentSizeOffset, _segmentSize);
            _accessor.Write(TotalFramesOffset, 0L);
        }
    }
    
    /// <summary>
    /// Try to write a frame to the ring buffer
    /// </summary>
    /// <returns>True if written successfully, false if buffer is full</returns>
    public bool TryWriteFrame(ReadOnlySpan<byte> frameData, out long frameIndex)
    {
        if (frameData.Length > DataBufferSize / 2)
        {
            throw new ArgumentException($"Frame size {frameData.Length} exceeds maximum {DataBufferSize / 2}", nameof(frameData));
        }
        
        lock (_lock)
        {
            long writePos = _accessor.ReadInt64(WritePositionOffset);
            long readPos = _accessor.ReadInt64(ReadPositionOffset);
            
            // Check available space (ring buffer)
            long used = writePos >= readPos 
                ? writePos - readPos 
                : DataBufferSize - readPos + writePos;
            
            long requiredSpace = 4 + frameData.Length;  // 4 bytes for length prefix
            
            if (used + requiredSpace > DataBufferSize)
            {
                frameIndex = -1;
                return false;  // Buffer full - backpressure
            }
            
            // Write frame length
            int offset = _headerSize + (int)(writePos % DataBufferSize);
            WriteInt32Wrapped(offset, frameData.Length);
            
            // Write frame data (may wrap around)
            offset = (offset + 4) % DataBufferSize;
            WriteDataWrapped(_headerSize + offset, frameData);
            
            // Update write position
            writePos = (writePos + requiredSpace) % DataBufferSize;
            _accessor.Write(WritePositionOffset, writePos);
            
            // Increment frame counter
            long totalFrames = _accessor.ReadInt64(TotalFramesOffset);
            frameIndex = totalFrames;
            _accessor.Write(TotalFramesOffset, totalFrames + 1);
            
            return true;
        }
    }
    
    /// <summary>
    /// Try to read a frame from the ring buffer
    /// </summary>
    /// <returns>True if frame was read, false if buffer is empty</returns>
    public bool TryReadFrame(out byte[] frameData)
    {
        lock (_lock)
        {
            long writePos = _accessor.ReadInt64(WritePositionOffset);
            long readPos = _accessor.ReadInt64(ReadPositionOffset);
            
            // Check if data available
            if (writePos == readPos)
            {
                frameData = Array.Empty<byte>();
                return false;  // Buffer empty
            }
            
            // Read frame length
            int offset = _headerSize + (int)(readPos % DataBufferSize);
            int frameLength = ReadInt32Wrapped(offset);
            
            if (frameLength <= 0 || frameLength > DataBufferSize / 2)
            {
                // Corrupted data - reset read position to write position
                _accessor.Write(ReadPositionOffset, writePos);
                frameData = Array.Empty<byte>();
                return false;
            }
            
            // Read frame data
            offset = (offset + 4) % DataBufferSize;
            frameData = new byte[frameLength];
            ReadDataWrapped(_headerSize + offset, frameData);
            
            // Update read position
            readPos = (readPos + 4 + frameLength) % DataBufferSize;
            _accessor.Write(ReadPositionOffset, readPos);
            
            return true;
        }
    }
    
    /// <summary>
    /// Get current buffer statistics
    /// </summary>
    public (long writePos, long readPos, long used, int capacity) GetStatistics()
    {
        lock (_lock)
        {
            long writePos = _accessor.ReadInt64(WritePositionOffset);
            long readPos = _accessor.ReadInt64(ReadPositionOffset);
            long used = writePos >= readPos 
                ? writePos - readPos 
                : DataBufferSize - readPos + writePos;
            
            return (writePos, readPos, used, DataBufferSize);
        }
    }
    
    private void WriteString(int offset, string value, int maxLength)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, maxLength);
        _accessor.WriteArray(offset, bytes, 0, length);
    }
    
    private void WriteInt32Wrapped(int offset, int value)
    {
        _accessor.Write(offset, value);
    }
    
    private int ReadInt32Wrapped(int offset)
    {
        return _accessor.ReadInt32(offset);
    }
    
    private void WriteDataWrapped(int startOffset, ReadOnlySpan<byte> data)
    {
        int remaining = DataBufferSize - startOffset;
        
        if (remaining >= data.Length)
        {
            // No wrap - single write
            _accessor.WriteArray(startOffset, data.ToArray(), 0, data.Length);
        }
        else
        {
            // Wrap around - two writes
            _accessor.WriteArray(startOffset, data.Slice(0, remaining).ToArray(), 0, remaining);
            _accessor.WriteArray(_headerSize, data.Slice(remaining).ToArray(), 0, data.Length - remaining);
        }
    }
    
    private void ReadDataWrapped(int startOffset, byte[] buffer)
    {
        int remaining = DataBufferSize - startOffset;
        
        if (remaining >= buffer.Length)
        {
            // No wrap - single read
            _accessor.ReadArray(startOffset, buffer, 0, buffer.Length);
        }
        else
        {
            // Wrap around - two reads
            _accessor.ReadArray(startOffset, buffer, 0, remaining);
            _accessor.ReadArray(_headerSize, buffer, remaining, buffer.Length - remaining);
        }
    }
    
    public void Dispose()
    {
        _accessor?.Dispose();
    }
}
