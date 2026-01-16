using System.Collections.Concurrent;

namespace ComCross.Core.Services;

/// <summary>
/// Caches protocol frame splitting information to avoid redundant parsing.
/// Implements LRU (Least Recently Used) eviction policy.
/// </summary>
public sealed class ProtocolFrameCacheService
{
    private readonly int _maxCacheSize;
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache;
    private readonly LinkedList<CacheKey> _lruList;
    private readonly object _lruLock = new();

    public ProtocolFrameCacheService(int maxCacheSize = 10000)
    {
        _maxCacheSize = maxCacheSize;
        _cache = new ConcurrentDictionary<CacheKey, CacheEntry>();
        _lruList = new LinkedList<CacheKey>();
    }

    /// <summary>
    /// Tries to get cached frame information.
    /// </summary>
    public bool TryGet(long messageId, string protocolId, out List<FrameInfo>? frames)
    {
        var key = new CacheKey(messageId, protocolId);

        if (_cache.TryGetValue(key, out var entry))
        {
            // Update LRU position
            lock (_lruLock)
            {
                _lruList.Remove(entry.LruNode);
                entry.LruNode = _lruList.AddFirst(key);
            }

            frames = entry.Frames;
            return true;
        }

        frames = null;
        return false;
    }

    /// <summary>
    /// Adds frame information to the cache.
    /// </summary>
    public void Add(long messageId, string protocolId, List<FrameInfo> frames)
    {
        var key = new CacheKey(messageId, protocolId);

        lock (_lruLock)
        {
            // Check cache size and evict if necessary
            while (_lruList.Count >= _maxCacheSize && _lruList.Last != null)
            {
                var evictKey = _lruList.Last.Value;
                _lruList.RemoveLast();
                _cache.TryRemove(evictKey, out _);
            }

            // Add new entry
            var lruNode = _lruList.AddFirst(key);
            var entry = new CacheEntry
            {
                Frames = frames,
                LruNode = lruNode
            };

            _cache[key] = entry;
        }
    }

    /// <summary>
    /// Checks if a frame is cached.
    /// </summary>
    public bool IsCached(long messageId, string protocolId)
    {
        var key = new CacheKey(messageId, protocolId);
        return _cache.ContainsKey(key);
    }

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    public void Clear()
    {
        lock (_lruLock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            EntryCount = _cache.Count,
            MaxSize = _maxCacheSize,
            UtilizationPercentage = (_cache.Count / (double)_maxCacheSize) * 100
        };
    }

    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        public long MessageId { get; }
        public string ProtocolId { get; }

        public CacheKey(long messageId, string protocolId)
        {
            MessageId = messageId;
            ProtocolId = protocolId;
        }

        public bool Equals(CacheKey other)
        {
            return MessageId == other.MessageId && ProtocolId == other.ProtocolId;
        }

        public override bool Equals(object? obj)
        {
            return obj is CacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MessageId, ProtocolId);
        }
    }

    private sealed class CacheEntry
    {
        public required List<FrameInfo> Frames { get; init; }
        public required LinkedListNode<CacheKey> LruNode { get; set; }
    }
}

/// <summary>
/// Represents frame splitting information for a protocol message.
/// Only stores position information, not parsed content.
/// </summary>
public sealed class FrameInfo
{
    /// <summary>
    /// Index of this frame within the physical message (0-based).
    /// </summary>
    public int FrameIndex { get; init; }

    /// <summary>
    /// Byte offset in the physical raw_data.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Length of the protocol frame in bytes.
    /// </summary>
    public int Length { get; init; }
}

/// <summary>
/// Represents a protocol frame extracted from physical data.
/// </summary>
public sealed class ProtocolFrame
{
    /// <summary>
    /// Frame index within the physical message.
    /// </summary>
    public int FrameIndex { get; init; }

    /// <summary>
    /// Byte offset in the physical raw_data.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Length of the frame in bytes.
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// Raw data for this protocol frame (extracted from physical frame).
    /// </summary>
    public required byte[] RawData { get; init; }

    /// <summary>
    /// Lazily parsed protocol-specific data.
    /// Null until Parse() is called.
    /// </summary>
    public object? ParsedData { get; set; }
}

/// <summary>
/// Cache statistics for monitoring and optimization.
/// </summary>
public sealed class CacheStatistics
{
    public int EntryCount { get; init; }
    public int MaxSize { get; init; }
    public double UtilizationPercentage { get; init; }
}
