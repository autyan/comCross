using ComCross.Shared.Services;
using ComCross.Shared.Models;
using ComCross.Platform.SharedMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.IO;

namespace ComCross.Tests.Core;

/// <summary>
/// 共享内存架构单元测试
/// 重点测试：边界检查、越界保护、环形缓冲区
/// </summary>
public class SharedMemoryTests : IDisposable
{
    private static readonly ISharedMemoryMapFactory MapFactory = new SharedMemoryMapFactory();
    private readonly SegmentedSharedMemory _sharedMemory;
    
    public SharedMemoryTests()
    {
        _sharedMemory = new SegmentedSharedMemory(
            "ComCross_Test_SharedMemory",
            10 * 1024 * 1024, // 10MB
            NullLogger<SegmentedSharedMemory>.Instance,
            MapFactory);
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
        Assert.Equal(100 + 4 + 16, 1024 * 1024 - 256 - segment.GetFreeSpace()); // 数据 + 长度字段 + wire header
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
        Assert.True(segment.GetFreeSpace() < 120); // 剩余空间不足以写入100字节 + 4字节长度 + 16字节header
        
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
    public void MapDescriptor_AndSegmentInfo_CanReopenExistingSegmentWithoutReinit()
    {
        // Arrange
        var backingFile = Path.Combine(Path.GetTempPath(), "comcross-tests", $"{Guid.NewGuid():N}.mmf");
        var name = $"ComCross_Test_SharedMemory_{Guid.NewGuid():N}";
        var totalSize = 2 * 1024 * 1024;
        var segmentSize = 64 * 1024;
        var sessionId = "session1";

        using var sharedMemory = new SegmentedSharedMemory(
            name,
            totalSize,
            NullLogger<SegmentedSharedMemory>.Instance,
            MapFactory,
            unixFilePath: backingFile,
            useFileBackedOnUnix: true,
            deleteUnixFileOnDispose: true);

        var segment1 = sharedMemory.AllocateSegment(sessionId, segmentSize);
        byte[] originalData = new byte[128];
        new Random().NextBytes(originalData);
        Assert.True(segment1.TryWriteFrame(originalData, out var writtenFrameId));
        Assert.Equal(1, writtenFrameId);

        Assert.True(sharedMemory.TryGetSegmentInfo(sessionId, out var offset, out var actualSegmentSize));
        Assert.Equal(segmentSize, actualSegmentSize);
        var map = sharedMemory.MapDescriptor;
        Assert.NotNull(map.UnixFilePath);

        // Act: reopen the underlying mapping (simulating another process) and open the same segment range.
        var reopenFactory = new SharedMemoryMapFactory();
        using var reopenedHandle = reopenFactory.Create(
            new SharedMemoryMapOptions(
                Name: map.Name,
                CapacityBytes: map.CapacityBytes,
                UnixFilePath: map.UnixFilePath,
                UseFileBackedOnUnix: true,
                DeleteUnixFileOnDispose: false));

        using var accessor2 = reopenedHandle.Map.CreateViewAccessor(offset, actualSegmentSize);
        using var segment2 = new SessionSegment(sessionId, accessor2, actualSegmentSize, logger: null, initializeHeader: false);

        // Assert: reading via reopened segment sees the data and updates the shared header positions.
        Assert.True(segment2.TryReadFrame(out var readData));
        Assert.Equal(originalData, readData);
        Assert.False(segment1.TryReadFrame(out _));
    }

    [Fact]
    public async Task SharedMemoryManager_TryGetSegmentDescriptor_ReturnsExpectedValues()
    {
        // Arrange
        var backingFile = Path.Combine(Path.GetTempPath(), "comcross-tests", $"{Guid.NewGuid():N}.mmf");
        var config = new SharedMemoryConfig
        {
            Name = $"ComCross_Test_Manager_{Guid.NewGuid():N}",
            UnixFilePath = backingFile,
            UseFileBackedOnUnix = true,
            DeleteUnixFileOnDispose = true,
            MaxTotalMemory = 4 * 1024 * 1024,
            MinSegmentSize = 256 * 1024,
        };

        using var manager = new SharedMemoryManager(config, NullLoggerFactory.Instance, new SharedMemoryMapFactory());
        manager.Initialize();

        // Act
        await manager.AllocateSegmentAsync("session1", requestedSize: 512 * 1024);
        var ok = manager.TryGetSegmentDescriptor("session1", out SharedMemorySegmentDescriptor descriptor);

        // Assert
        Assert.True(ok);
        Assert.Equal(config.Name, descriptor.MapName);
        Assert.Equal(config.MaxTotalMemory, descriptor.MapCapacityBytes);
        Assert.Equal(config.UnixFilePath, descriptor.UnixFilePath);
        Assert.True(descriptor.SegmentOffset > 0);
        Assert.True(descriptor.SegmentSize > 0);
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
        byte[] data = new byte[400]; // 每帧420字节（含长度+16字节header）
        
        // Act：写入4帧（1680字节），然后读取2帧（840字节），再写入2帧（840字节）
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
        Assert.Equal(1020, stats.UsedSize); // 1000字节数据 + 4字节长度 + 16字节header
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
            NullLogger<SegmentedSharedMemory>.Instance,
            MapFactory);
            
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
