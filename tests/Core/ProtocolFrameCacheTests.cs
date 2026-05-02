using System;
using System.Collections.Generic;
using System.Linq;
using ComCross.Core.Services;
using Xunit;

namespace ComCross.Tests.Core;

/// <summary>
/// Tests for ProtocolFrameCacheService (LRU cache for frame splitting).
/// </summary>
public sealed class ProtocolFrameCacheTests
{
    [Fact]
    public void Cache_Add_AndRetrieve_WorksCorrectly()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);
        var messageId = 1L;
        var protocolId = "modbus-rtu";
        var frames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 8 },
            new() { FrameIndex = 1, Offset = 8, Length = 10 }
        };

        // Act
        cache.Add(messageId, protocolId, frames);
        var found = cache.TryGet(messageId, protocolId, out var retrieved);

        // Assert
        Assert.True(found);
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Count);
        Assert.Equal(0, retrieved[0].Offset);
        Assert.Equal(8, retrieved[0].Length);
        Assert.Equal(8, retrieved[1].Offset);
        Assert.Equal(10, retrieved[1].Length);
    }

    [Fact]
    public void Cache_Miss_ReturnsNull()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);

        // Act
        var found = cache.TryGet(999L, "unknown-protocol", out var retrieved);

        // Assert
        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void Cache_LRU_Eviction_WorksCorrectly()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 3);

        // Add 3 entries (fills cache)
        for (int i = 1; i <= 3; i++)
        {
            cache.Add(i, "protocol", new List<FrameInfo>
            {
                new() { FrameIndex = 0, Offset = 0, Length = i }
            });
        }

        // Act: Add 4th entry (should evict entry 1)
        cache.Add(4, "protocol", new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 4 }
        });

        // Assert: Entry 1 should be evicted
        Assert.False(cache.TryGet(1, "protocol", out _));
        Assert.True(cache.TryGet(2, "protocol", out _));
        Assert.True(cache.TryGet(3, "protocol", out _));
        Assert.True(cache.TryGet(4, "protocol", out _));
    }

    [Fact]
    public void Cache_LRU_Access_UpdatesOrder()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 3);

        // Add 3 entries
        for (int i = 1; i <= 3; i++)
        {
            cache.Add(i, "protocol", new List<FrameInfo>
            {
                new() { FrameIndex = 0, Offset = 0, Length = i }
            });
        }

        // Access entry 1 (moves it to front of LRU list)
        cache.TryGet(1, "protocol", out _);

        // Act: Add 4th entry (should evict entry 2, not 1)
        cache.Add(4, "protocol", new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 4 }
        });

        // Assert: Entry 2 should be evicted (was oldest), entry 1 should remain
        Assert.True(cache.TryGet(1, "protocol", out _));
        Assert.False(cache.TryGet(2, "protocol", out _));
        Assert.True(cache.TryGet(3, "protocol", out _));
        Assert.True(cache.TryGet(4, "protocol", out _));
    }

    [Fact]
    public void Cache_DifferentProtocols_StoredSeparately()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);
        var messageId = 1L;
        
        var modbusFrames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 8 }
        };
        
        var asciiFrames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 20 }
        };

        // Act
        cache.Add(messageId, "modbus-rtu", modbusFrames);
        cache.Add(messageId, "ascii-text", asciiFrames);

        cache.TryGet(messageId, "modbus-rtu", out var modbusRetrieved);
        cache.TryGet(messageId, "ascii-text", out var asciiRetrieved);

        // Assert
        Assert.NotNull(modbusRetrieved);
        Assert.NotNull(asciiRetrieved);
        Assert.Equal(8, modbusRetrieved[0].Length);
        Assert.Equal(20, asciiRetrieved[0].Length);
    }

    [Fact]
    public void Cache_IsCached_WorksCorrectly()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);
        var frames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 8 }
        };

        // Act
        cache.Add(1, "protocol", frames);

        // Assert
        Assert.True(cache.IsCached(1, "protocol"));
        Assert.False(cache.IsCached(2, "protocol"));
        Assert.False(cache.IsCached(1, "other-protocol"));
    }

    [Fact]
    public void Cache_Clear_RemovesAllEntries()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);
        var frames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 8 }
        };

        for (int i = 1; i <= 10; i++)
        {
            cache.Add(i, "protocol", frames);
        }

        // Act
        cache.Clear();

        // Assert
        var stats = cache.GetStatistics();
        Assert.Equal(0, stats.EntryCount);

        for (int i = 1; i <= 10; i++)
        {
            Assert.False(cache.IsCached(i, "protocol"));
        }
    }

    [Fact]
    public void Cache_GetStatistics_ReturnsCorrectInfo()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);
        var frames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 8 }
        };

        // Add 25 entries
        for (int i = 1; i <= 25; i++)
        {
            cache.Add(i, "protocol", frames);
        }

        // Act
        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(25, stats.EntryCount);
        Assert.Equal(100, stats.MaxSize);
        Assert.Equal(25.0, stats.UtilizationPercentage);
    }

    [Fact]
    public void Cache_MultipleFrames_PreservesOrder()
    {
        // Arrange
        var cache = new ProtocolFrameCacheService(maxCacheSize: 100);
        var frames = new List<FrameInfo>
        {
            new() { FrameIndex = 0, Offset = 0, Length = 8 },
            new() { FrameIndex = 1, Offset = 8, Length = 10 },
            new() { FrameIndex = 2, Offset = 18, Length = 6 },
            new() { FrameIndex = 3, Offset = 24, Length = 12 }
        };

        // Act
        cache.Add(1, "protocol", frames);
        cache.TryGet(1, "protocol", out var retrieved);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(4, retrieved.Count);
        
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, retrieved[i].FrameIndex);
        }
    }
}
