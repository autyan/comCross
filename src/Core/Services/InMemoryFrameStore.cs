using System.Collections.Concurrent;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class InMemoryFrameStore : IFrameStore
{
    private const long DefaultMaxBytesPerSession = 64L * 1024 * 1024;
    private const int CompactThreshold = 2048;

    private readonly ILogger<InMemoryFrameStore> _logger;
    private readonly long _maxBytesPerSession;
    private readonly ConcurrentDictionary<string, SessionWindow> _sessions = new(StringComparer.Ordinal);

    public InMemoryFrameStore(ILogger<InMemoryFrameStore> logger)
        : this(logger, DefaultMaxBytesPerSession)
    {
    }

    public InMemoryFrameStore(ILogger<InMemoryFrameStore> logger, long maxBytesPerSession)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxBytesPerSession = maxBytesPerSession <= 0 ? DefaultMaxBytesPerSession : maxBytesPerSession;
    }

    public event Action<string>? FramesAppended;

    public long Append(string sessionId, DateTime timestampUtc, FrameDirection direction, byte[] rawData, MessageFormat format, string source)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Missing sessionId.", nameof(sessionId));
        }

        rawData ??= Array.Empty<byte>();
        source ??= string.Empty;

        var window = _sessions.GetOrAdd(sessionId, _ => new SessionWindow(_maxBytesPerSession));
        var frameId = window.Append(sessionId, timestampUtc, direction, rawData, format, source);

        try
        {
            FramesAppended?.Invoke(sessionId);
        }
        catch
        {
        }

        return frameId;
    }

    public IReadOnlyList<FrameRecord> ReadAfter(string sessionId, long afterFrameId, int maxCount, out long firstAvailableFrameId)
    {
        firstAvailableFrameId = 0;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Array.Empty<FrameRecord>();
        }

        if (!_sessions.TryGetValue(sessionId, out var window))
        {
            return Array.Empty<FrameRecord>();
        }

        return window.ReadAfter(afterFrameId, maxCount, out firstAvailableFrameId);
    }

    public (long FirstAvailableFrameId, long LastFrameId, long DroppedFrames) GetWindowInfo(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (0, 0, 0);
        }

        if (!_sessions.TryGetValue(sessionId, out var window))
        {
            return (0, 0, 0);
        }

        return window.GetInfo();
    }

    public void Clear(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_sessions.TryGetValue(sessionId, out var window))
        {
            window.Clear();
        }
    }

    private sealed class SessionWindow
    {
        private readonly long _maxBytes;

        private readonly object _gate = new();
        private readonly List<FrameRecord> _frames = new();
        private int _headIndex;
        private long _firstId = 1;
        private long _nextId = 1;
        private long _bytes;
        private long _dropped;

        public SessionWindow(long maxBytes)
        {
            _maxBytes = maxBytes;
        }

        public long Append(string sessionId, DateTime timestampUtc, FrameDirection direction, byte[] rawData, MessageFormat format, string source)
        {
            lock (_gate)
            {
                var id = _nextId++;
                var record = new FrameRecord(id, sessionId, timestampUtc, direction, rawData, format, source);
                _frames.Add(record);
                _bytes += rawData.Length;

                EvictIfNeeded();
                MaybeCompact();
                return id;
            }
        }

        public IReadOnlyList<FrameRecord> ReadAfter(long afterFrameId, int maxCount, out long firstAvailableFrameId)
        {
            lock (_gate)
            {
                firstAvailableFrameId = _firstId;
                if (maxCount <= 0)
                {
                    return Array.Empty<FrameRecord>();
                }

                var lastId = _nextId - 1;
                var startId = afterFrameId + 1;
                if (startId < _firstId)
                {
                    startId = _firstId;
                }

                if (startId > lastId)
                {
                    return Array.Empty<FrameRecord>();
                }

                var offsetFromFirst = startId - _firstId;
                var startIndex = _headIndex + (int)offsetFromFirst;
                if (startIndex < _headIndex || startIndex >= _frames.Count)
                {
                    return Array.Empty<FrameRecord>();
                }

                var take = Math.Min(maxCount, _frames.Count - startIndex);
                if (take <= 0)
                {
                    return Array.Empty<FrameRecord>();
                }

                // Copy to avoid exposing mutable list.
                return _frames.GetRange(startIndex, take);
            }
        }

        public (long FirstAvailableFrameId, long LastFrameId, long DroppedFrames) GetInfo()
        {
            lock (_gate)
            {
                return (_firstId, _nextId - 1, _dropped);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _frames.Clear();
                _headIndex = 0;
                _firstId = 1;
                _nextId = 1;
                _bytes = 0;
                _dropped = 0;
            }
        }

        private void EvictIfNeeded()
        {
            while (_bytes > _maxBytes && _headIndex < _frames.Count)
            {
                var victim = _frames[_headIndex];
                _bytes -= victim.RawData?.Length ?? 0;
                _headIndex++;
                _firstId++;
                _dropped++;
            }
        }

        private void MaybeCompact()
        {
            if (_headIndex < CompactThreshold)
            {
                return;
            }

            // Avoid O(n) shift on every eviction.
            _frames.RemoveRange(0, _headIndex);
            _headIndex = 0;
        }
    }
}
