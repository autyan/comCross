using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
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

    // Frame payload wire header (stored inside the ring buffer frame data).
    // Base: [version:u16][flags:u8][reserved:u8][timestampUtcTicks:i64][rawLen:i32]
    // With attributes: Base + [attrSectionLen:i32][attrSection][rawData]
    // attrSection: [count:u8] repeated [keyLen:u8][valueLen:u16][keyUtf8][valueUtf8]
    private const ushort WIRE_VERSION = 1;
    private const byte WIRE_FLAG_ATTRIBUTES = 0x01;
    private const int WIRE_BASE_HEADER_SIZE = 16;
    private const int WIRE_ATTRIBUTE_LENGTH_SIZE = 4;

    public readonly record struct SharedMemoryFrameRecord(
        DateTime TimestampUtc,
        byte[] RawData,
        IReadOnlyDictionary<string, string> Attributes,
        int AttributeSchemaVersion = MessageFrameAttributes.SchemaVersion);
    
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
        => TryWriteFrame(data, attributes: null, out frameId);

    public bool TryWriteFrame(ReadOnlySpan<byte> data, IReadOnlyDictionary<string, string>? attributes, out long frameId)
    {
        frameId = -1;
        
        if (_disposed)
        {
            _logger?.LogWarning("[{SessionId}] SessionSegment已释放，拒绝写入", _sessionId);
            return false;
        }
        
        var normalizedAttributes = MessageFrameAttributes.Normalize(
            attributes,
            diagnostic => _logger?.LogDebug("[{SessionId}] Dropped message frame attribute: {Diagnostic}", _sessionId, diagnostic));
        var attributeBytes = EncodeAttributes(normalizedAttributes);

        // 帧格式：[Length:4字节][WireBaseHeader:16字节][AttrLen:4字节][AttrSection:N字节][RawData:N字节]
        int recordLength = WIRE_BASE_HEADER_SIZE + WIRE_ATTRIBUTE_LENGTH_SIZE + attributeBytes.Length + data.Length;
        int frameSize = 4 + recordLength;
        
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
        WriteInt32Wrapped(logicalWritePos, recordLength);

        // 写入WireHeader + RawData（处理环绕）
        int recordWritePos = (int)((writePosition + 4) % _dataSize);

        Span<byte> header = stackalloc byte[WIRE_BASE_HEADER_SIZE];
        BinaryPrimitives.WriteUInt16LittleEndian(header, WIRE_VERSION);
        header[2] = normalizedAttributes.Count > 0 ? WIRE_FLAG_ATTRIBUTES : (byte)0; // flags
        header[3] = 0; // reserved
        BinaryPrimitives.WriteInt64LittleEndian(header.Slice(4, 8), DateTime.UtcNow.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(12, 4), data.Length);

        WriteBytesWrapped(recordWritePos, header);
        var attrLengthPos = (recordWritePos + WIRE_BASE_HEADER_SIZE) % _dataSize;
        WriteInt32Wrapped(attrLengthPos, attributeBytes.Length);

        if (attributeBytes.Length > 0)
        {
            WriteBytesWrapped((attrLengthPos + WIRE_ATTRIBUTE_LENGTH_SIZE) % _dataSize, attributeBytes);
        }

        var rawWritePos = (attrLengthPos + WIRE_ATTRIBUTE_LENGTH_SIZE + attributeBytes.Length) % _dataSize;
        WriteBytesWrapped(rawWritePos, data);
        
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
        // Compatibility wrapper: returns only RawData payload.
        if (!TryReadFrameRecord(out var record))
        {
            data = Array.Empty<byte>();
            return false;
        }

        data = record.RawData;
        return true;
    }

    /// <summary>
    /// Reads a wire record and returns decoded metadata + raw payload.
    /// </summary>
    public bool TryReadFrameRecord(out SharedMemoryFrameRecord record)
    {
        record = default;

        if (_disposed)
        {
            return false;
        }

        long writePosition = ReadWritePosition();
        long readPosition = ReadReadPosition();

        if (readPosition >= writePosition)
        {
            return false;
        }

        int logicalReadPos = (int)(readPosition % _dataSize);
        int recordLength = ReadInt32Wrapped(logicalReadPos);
        if (recordLength < WIRE_BASE_HEADER_SIZE || recordLength > _dataSize)
        {
            _logger?.LogError(
                "[{SessionId}] 读取到非法长度：{Length}，跳过此帧",
                _sessionId, recordLength);
            return false;
        }

        int recordReadPos = (int)((readPosition + 4) % _dataSize);

        // Read header
        Span<byte> header = stackalloc byte[WIRE_BASE_HEADER_SIZE];
        ReadBytesWrapped(recordReadPos, header);

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (version != WIRE_VERSION)
        {
            _logger?.LogError(
                "[{SessionId}] Wire version mismatch: {Version}",
                _sessionId, version);
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(4, 8));
        var rawLen = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(12, 4));

        if (rawLen < 0 || rawLen > _dataSize)
        {
            _logger?.LogError(
                "[{SessionId}] 读取到非法payload长度：{Length}，跳过此帧",
                _sessionId, rawLen);
            return false;
        }

        var attributes = MessageFrameAttributes.Empty;
        var rawReadPos = (recordReadPos + WIRE_BASE_HEADER_SIZE) % _dataSize;
        if (recordLength == WIRE_BASE_HEADER_SIZE + rawLen)
        {
            // Compatible read path for frames written before attributes existed.
        }
        else
        {
            if (recordLength < WIRE_BASE_HEADER_SIZE + WIRE_ATTRIBUTE_LENGTH_SIZE + rawLen)
            {
                _logger?.LogError(
                    "[{SessionId}] 读取到非法frame长度：{Length}",
                    _sessionId, recordLength);
                return false;
            }

            var attrLengthPos = (recordReadPos + WIRE_BASE_HEADER_SIZE) % _dataSize;
            var attrLen = ReadInt32Wrapped(attrLengthPos);
            if (attrLen < 0 || recordLength != WIRE_BASE_HEADER_SIZE + WIRE_ATTRIBUTE_LENGTH_SIZE + attrLen + rawLen)
            {
                _logger?.LogError(
                    "[{SessionId}] 读取到非法attribute长度：{Length}",
                    _sessionId, attrLen);
                return false;
            }

            if (attrLen > 0)
            {
                var attrReadPos = (attrLengthPos + WIRE_ATTRIBUTE_LENGTH_SIZE) % _dataSize;
                attributes = DecodeAttributes(ReadDataWrapped(attrReadPos, attrLen));
            }

            rawReadPos = (attrLengthPos + WIRE_ATTRIBUTE_LENGTH_SIZE + attrLen) % _dataSize;
        }

        var raw = ReadDataWrapped(rawReadPos, rawLen);

        // advance
        readPosition += 4 + recordLength;
        WriteReadPosition(readPosition);

        record = new SharedMemoryFrameRecord(new DateTime(ticks, DateTimeKind.Utc), raw, attributes);
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

    private static byte[] EncodeAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.Count == 0)
        {
            return Array.Empty<byte>();
        }

        using var stream = new MemoryStream();
        stream.WriteByte((byte)attributes.Count);
        Span<byte> valueLength = stackalloc byte[2];

        foreach (var pair in attributes.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            var keyBytes = Encoding.UTF8.GetBytes(pair.Key);
            var valueBytes = Encoding.UTF8.GetBytes(pair.Value);
            stream.WriteByte((byte)keyBytes.Length);

            BinaryPrimitives.WriteUInt16LittleEndian(valueLength, (ushort)valueBytes.Length);
            stream.Write(valueLength);

            stream.Write(keyBytes);
            stream.Write(valueBytes);
        }

        return stream.ToArray();
    }

    private IReadOnlyDictionary<string, string> DecodeAttributes(byte[] encoded)
    {
        if (encoded.Length == 0)
        {
            return MessageFrameAttributes.Empty;
        }

        try
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var index = 0;
            var count = encoded[index++];

            for (var i = 0; i < count; i++)
            {
                if (index + 3 > encoded.Length)
                {
                    return MessageFrameAttributes.Empty;
                }

                var keyLen = encoded[index++];
                var valueLen = BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(index, 2));
                index += 2;

                if (keyLen <= 0 || index + keyLen + valueLen > encoded.Length)
                {
                    return MessageFrameAttributes.Empty;
                }

                var key = Encoding.UTF8.GetString(encoded, index, keyLen);
                index += keyLen;
                var value = Encoding.UTF8.GetString(encoded, index, valueLen);
                index += valueLen;

                result[key] = value;
            }

            return MessageFrameAttributes.Normalize(
                result,
                diagnostic => _logger?.LogDebug("[{SessionId}] Dropped decoded message frame attribute: {Diagnostic}", _sessionId, diagnostic));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[{SessionId}] Failed to decode message frame attributes.", _sessionId);
            return MessageFrameAttributes.Empty;
        }
    }
    
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
    private void WriteBytesWrapped(int position, ReadOnlySpan<byte> data)
    {
        int remaining = data.Length;
        int sourceOffset = 0;

        byte[]? rented = null;
        
        try
        {
            while (remaining > 0)
            {
                int physicalPos = _dataOffset + ((position + sourceOffset) % _dataSize);
                int chunkSize = Math.Min(remaining, _dataSize - (physicalPos - _dataOffset));

                if (rented is null || rented.Length < chunkSize)
                {
                    if (rented is not null)
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }

                    rented = ArrayPool<byte>.Shared.Rent(chunkSize);
                }

                data.Slice(sourceOffset, chunkSize).CopyTo(rented.AsSpan(0, chunkSize));
                _accessor.WriteArray(physicalPos, rented, 0, chunkSize);

                sourceOffset += chunkSize;
                remaining -= chunkSize;
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private void ReadBytesWrapped(int position, Span<byte> destination)
    {
        int remaining = destination.Length;
        int destOffset = 0;

        while (remaining > 0)
        {
            int physicalPos = _dataOffset + ((position + destOffset) % _dataSize);
            int chunkSize = Math.Min(remaining, _dataSize - (physicalPos - _dataOffset));

            // MemoryMappedViewAccessor does not support Span, so read into a temp array.
            var tmp = ArrayPool<byte>.Shared.Rent(chunkSize);
            try
            {
                _accessor.ReadArray(physicalPos, tmp, 0, chunkSize);
                tmp.AsSpan(0, chunkSize).CopyTo(destination.Slice(destOffset, chunkSize));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tmp);
            }

            destOffset += chunkSize;
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
