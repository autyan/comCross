using ComCross.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

/// <summary>
/// 共享内存架构单元测试
/// 重点测试：边界检查、越界保护、环形缓冲区
/// </summary>
public class SharedMemoryTests : IDisposable
{
    private readonly SegmentedSharedMemory _sharedMemory;
    
    public SharedMemoryTests()
    {
        _sharedMemory = new SegmentedSharedMemory(
            "ComCross_Test_SharedMemory",
            10 * 1024 * 1024, // 10MB
            NullLogger<SegmentedSharedMemory>.Instance);
    }
    
    [Fact]
    public void AllocateSegment_Success()
    {
        // Arrange & Act
        var segment = _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        
        // Assert
        Assert.NotNull(segment);
        Assert.Equal(1024 * 1024 - 256, segment.GetFreeSpace()); // 减去Header
    }
    
    [Fact]
    public void AllocateSegment_DuplicateSession_ThrowsException()
    {
        // Arrange
        _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _sharedMemory.AllocateSegment("session1", 1024 * 1024));
    }
    
    [Fact]
    public void AllocateSegment_ExceedsMemory_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _sharedMemory.AllocateSegment("session1", 20 * 1024 * 1024)); // 超过10MB
    }
    
    [Fact]
    public void TryWriteFrame_Success()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        byte[] data = new byte[100];
        new Random().NextBytes(data);
        
        // Act
        bool result = segment.TryWriteFrame(data, out long frameId);
        
        // Assert
        Assert.True(result);
        Assert.Equal(1, frameId);
        Assert.Equal(100 + 4, 1024 * 1024 - 256 - segment.GetFreeSpace()); // 数据+长度字段
    }
    
    [Fact]
    public void TryWriteFrame_ExceedsSegmentSize_ReturnsFalse()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024); // 1KB
        byte[] data = new byte[2048]; // 2KB，超过Segment容量
        
        // Act
        bool result = segment.TryWriteFrame(data, out long frameId);
        
        // Assert ✅ 边界检查1：单帧过大
        Assert.False(result);
        Assert.Equal(-1, frameId);
    }
    
    [Fact]
    public void TryWriteFrame_FillsSegment_ReturnsFalseWhenFull()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024); // 1KB
        byte[] data = new byte[100];
        
        // Act：填满Segment
        int writeCount = 0;
        while (segment.TryWriteFrame(data, out _))
        {
            writeCount++;
            if (writeCount > 100) break; // 防止无限循环
        }
        
        // Assert ✅ 边界检查2：空间不足时返回false
        Assert.True(writeCount < 100); // 应该在某个点停止
        Assert.True(segment.GetFreeSpace() < 104); // 剩余空间不足以写入100字节+4字节长度
        
        // 再次尝试写入应该失败
        bool result = segment.TryWriteFrame(data, out long frameId);
        Assert.False(result);
    }
    
    [Fact]
    public void TryReadFrame_Success()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        byte[] originalData = new byte[100];
        new Random().NextBytes(originalData);
        segment.TryWriteFrame(originalData, out _);
        
        // Act
        bool result = segment.TryReadFrame(out byte[] readData);
        
        // Assert
        Assert.True(result);
        Assert.Equal(originalData.Length, readData.Length);
        Assert.Equal(originalData, readData);
    }
    
    [Fact]
    public void TryReadFrame_EmptySegment_ReturnsFalse()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        
        // Act
        bool result = segment.TryReadFrame(out byte[] data);
        
        // Assert
        Assert.False(result);
        Assert.Empty(data);
    }
    
    [Fact]
    public void WriteAndRead_MultipleFrames_PreservesOrder()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        List<byte[]> originalFrames = new();
        
        for (int i = 0; i < 10; i++)
        {
            byte[] data = new byte[50 + i * 10]; // 变化大小
            new Random().NextBytes(data);
            originalFrames.Add(data);
            segment.TryWriteFrame(data, out _);
        }
        
        // Act & Assert：按顺序读取
        for (int i = 0; i < 10; i++)
        {
            bool result = segment.TryReadFrame(out byte[] readData);
            Assert.True(result);
            Assert.Equal(originalFrames[i], readData);
        }
        
        // 全部读完后应该为空
        Assert.False(segment.TryReadFrame(out _));
    }
    
    [Fact]
    public void RingBuffer_Wraparound_WorksCorrectly()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 2048); // 2KB（实际数据区：2048-256=1792字节）
        byte[] data = new byte[400]; // 每帧404字节（含长度）
        
        // Act：写入4帧（1616字节），然后读取2帧（808字节），再写入2帧（808字节）
        // 这会触发环绕
        for (int i = 0; i < 4; i++)
        {
            Array.Fill(data, (byte)i);
            Assert.True(segment.TryWriteFrame(data, out _));
        }
        
        // 读取前2帧
        Assert.True(segment.TryReadFrame(out byte[] frame0));
        Assert.True(segment.TryReadFrame(out byte[] frame1));
        Assert.All(frame0, b => Assert.Equal(0, b));
        Assert.All(frame1, b => Assert.Equal(1, b));
        
        // 再写入2帧（触发环绕）
        for (int i = 4; i < 6; i++)
        {
            Array.Fill(data, (byte)i);
            Assert.True(segment.TryWriteFrame(data, out _));
        }
        
        // 读取剩余帧，验证数据正确性
        Assert.True(segment.TryReadFrame(out byte[] frame2));
        Assert.All(frame2, b => Assert.Equal(2, b));
        
        Assert.True(segment.TryReadFrame(out byte[] frame3));
        Assert.All(frame3, b => Assert.Equal(3, b));
        
        Assert.True(segment.TryReadFrame(out byte[] frame4));
        Assert.All(frame4, b => Assert.Equal(4, b));
        
        Assert.True(segment.TryReadFrame(out byte[] frame5));
        Assert.All(frame5, b => Assert.Equal(5, b));
    }
    
    [Fact]
    public void GetUsageStats_ReturnsCorrectValues()
    {
        // Arrange
        _sharedMemory.AllocateSegment("session1", 1 * 1024 * 1024); // 1MB
        _sharedMemory.AllocateSegment("session2", 2 * 1024 * 1024); // 2MB
        
        var segment1 = _sharedMemory.GetSegment("session1")!;
        segment1.TryWriteFrame(new byte[1000], out _);
        
        // Act
        var stats = _sharedMemory.GetUsageStats();
        
        // Assert
        Assert.Equal(10 * 1024 * 1024, stats.TotalSize);
        Assert.Equal(3 * 1024 * 1024, stats.AllocatedSize);
        Assert.Equal(1004, stats.UsedSize); // 1000字节数据 + 4字节长度
        Assert.Equal(2, stats.SessionCount);
    }
    
    [Fact]
    public void Backpressure_HighWatermark_DetectedCorrectly()
    {
        // Arrange
        var segment = _sharedMemory.AllocateSegment("session1", 1024); // 1KB
        byte[] data = new byte[100];
        
        // Act：填充到80%以上
        int writeCount = 0;
        while (segment.GetUsageRatio() < 0.8 && writeCount < 100)
        {
            if (!segment.TryWriteFrame(data, out _))
                break;
            writeCount++;
        }
        
        // Assert
        Assert.True(segment.GetUsageRatio() >= 0.8); // 应该达到高水位
        Assert.True(writeCount > 0);
    }
    
    [Fact]
    public void ConcurrentWriteAndRead_NoDataLoss()
    {
        // Arrange - 使用足够大的共享内存避免空间不足
        var largeSharedMemory = new SegmentedSharedMemory(
            "ComCross_Test_Large_SharedMemory",
            20 * 1024 * 1024, // 20MB，足够测试使用
            NullLogger<SegmentedSharedMemory>.Instance);
            
        var segment = largeSharedMemory.AllocateSegment("session1", 10 * 1024 * 1024); // 10MB
        const int totalFrames = 1000;
        int writeCount = 0;
        int readCount = 0;
        
        // Act：并发写入和读取
        var writeTask = Task.Run(() =>
        {
            for (int i = 0; i < totalFrames; i++)
            {
                byte[] data = BitConverter.GetBytes(i);
                while (!segment.TryWriteFrame(data, out _))
                {
                    Thread.Sleep(1);
                }
                Interlocked.Increment(ref writeCount);
            }
        });
        
        var readTask = Task.Run(() =>
        {
            while (readCount < totalFrames)
            {
                if (segment.TryReadFrame(out byte[] data))
                {
                    int value = BitConverter.ToInt32(data);
                    Assert.Equal(readCount, value); // 验证顺序
                    Interlocked.Increment(ref readCount);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        });
        
        Task.WaitAll(writeTask, readTask);
        
        // Assert
        Assert.Equal(totalFrames, writeCount);
        Assert.Equal(totalFrames, readCount);
        
        // Cleanup
        largeSharedMemory.Dispose();
    }
    
    [Fact]
    public void ReleaseSegment_Success()
    {
        // Arrange
        _sharedMemory.AllocateSegment("session1", 1024 * 1024);
        
        // Act
        _sharedMemory.ReleaseSegment("session1");
        
        // Assert
        Assert.Null(_sharedMemory.GetSegment("session1"));
        Assert.Empty(_sharedMemory.GetSessionIds());
    }
    
    public void Dispose()
    {
        _sharedMemory.Dispose();
    }
}
