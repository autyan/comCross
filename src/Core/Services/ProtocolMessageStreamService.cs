using System.Collections.Concurrent;
using ComCross.Core.Protocols;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// 协议消息流服务
/// 管理物理帧和协议解析后的消息
/// 支持协议切换和懒解析
/// </summary>
public sealed class ProtocolMessageStreamService
{
    private readonly ProtocolRegistry _protocolRegistry;
    private readonly ProtocolFrameCacheService _frameCache;
    private readonly ConcurrentDictionary<string, SessionProtocolStream> _streams = new();

    private static void ThrowIfInvalidSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId cannot be empty.", nameof(sessionId));
        }
    }

    public ProtocolMessageStreamService(
        ProtocolRegistry protocolRegistry,
        ProtocolFrameCacheService frameCacheService)
    {
        _protocolRegistry = protocolRegistry ?? throw new ArgumentNullException(nameof(protocolRegistry));
        _frameCache = frameCacheService ?? throw new ArgumentNullException(nameof(frameCacheService));
    }

    /// <summary>
    /// 添加物理帧（原始数据）
    /// </summary>
    public void AppendPhysicalFrame(string sessionId, PhysicalFrame frame)
    {
        ThrowIfInvalidSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(frame);

        var stream = _streams.GetOrAdd(sessionId, _ => new SessionProtocolStream(_protocolRegistry, _frameCache));
        stream.AppendPhysicalFrame(frame);
    }

    /// <summary>
    /// 获取解析后的协议消息（懒解析）
    /// </summary>
    public IReadOnlyList<ProtocolMessage> GetProtocolMessages(
        string sessionId, 
        string? protocolId, 
        int skip = 0, 
        int take = 100)
    {
        ThrowIfInvalidSessionId(sessionId);

        if (!_streams.TryGetValue(sessionId, out var stream))
        {
            return Array.Empty<ProtocolMessage>();
        }

        return stream.GetProtocolMessages(protocolId, skip, take);
    }

    /// <summary>
    /// 获取物理帧（原始数据）
    /// </summary>
    public IReadOnlyList<PhysicalFrame> GetPhysicalFrames(
        string sessionId,
        int skip = 0,
        int take = 100)
    {
        ThrowIfInvalidSessionId(sessionId);

        if (!_streams.TryGetValue(sessionId, out var stream))
        {
            return Array.Empty<PhysicalFrame>();
        }

        return stream.GetPhysicalFrames(skip, take);
    }

    /// <summary>
    /// 切换会话的活动协议
    /// </summary>
    public void SetActiveProtocol(string sessionId, string protocolId)
    {
        ThrowIfInvalidSessionId(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(protocolId);

        if (!_streams.TryGetValue(sessionId, out var stream))
        {
            throw new InvalidOperationException($"Session '{sessionId}' not found");
        }

        stream.SetActiveProtocol(protocolId);
    }

    /// <summary>
    /// 获取会话的活动协议ID
    /// </summary>
    public string? GetActiveProtocol(string sessionId)
    {
        ThrowIfInvalidSessionId(sessionId);

        if (!_streams.TryGetValue(sessionId, out var stream))
        {
            return null;
        }

        return stream.GetActiveProtocol();
    }

    /// <summary>
    /// 清除会话的所有数据
    /// </summary>
    public void Clear(string sessionId)
    {
        ThrowIfInvalidSessionId(sessionId);

        if (_streams.TryGetValue(sessionId, out var stream))
        {
            stream.Clear();
        }
    }

    /// <summary>
    /// 订阅新物理帧事件
    /// </summary>
    public IDisposable SubscribeToPhysicalFrames(string sessionId, Action<PhysicalFrame> handler)
    {
        ThrowIfInvalidSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(handler);

        var stream = _streams.GetOrAdd(sessionId, _ => new SessionProtocolStream(_protocolRegistry, _frameCache));
        return stream.SubscribeToPhysicalFrames(handler);
    }

    /// <summary>
    /// 订阅新协议消息事件（实时解析）
    /// </summary>
    public IDisposable SubscribeToProtocolMessages(string sessionId, string protocolId, Action<ProtocolMessage> handler)
    {
        ThrowIfInvalidSessionId(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(protocolId);
        ArgumentNullException.ThrowIfNull(handler);

        var stream = _streams.GetOrAdd(sessionId, _ => new SessionProtocolStream(_protocolRegistry, _frameCache));
        return stream.SubscribeToProtocolMessages(protocolId, handler);
    }

    /// <summary>
    /// 单个会话的协议流
    /// </summary>
    private sealed class SessionProtocolStream
    {
        private readonly ProtocolRegistry _protocolRegistry;
        private readonly ProtocolFrameCacheService _frameCache;
        private readonly List<PhysicalFrame> _physicalFrames = new();
        private readonly List<Action<PhysicalFrame>> _physicalFrameSubscribers = new();
        private readonly Dictionary<string, List<Action<ProtocolMessage>>> _protocolMessageSubscribers = new();
        private readonly object _lock = new();
        private string? _activeProtocolId;

        public SessionProtocolStream(ProtocolRegistry protocolRegistry, ProtocolFrameCacheService frameCache)
        {
            _protocolRegistry = protocolRegistry;
            _frameCache = frameCache;
        }

        public void AppendPhysicalFrame(PhysicalFrame frame)
        {
            lock (_lock)
            {
                _physicalFrames.Add(frame);

                // 通知物理帧订阅者
                foreach (var subscriber in _physicalFrameSubscribers)
                {
                    try
                    {
                        subscriber(frame);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"ProtocolMessageStream: Physical frame subscriber error: {ex.Message}");
                    }
                }

                // 如果有活动协议，实时解析并通知
                if (!string.IsNullOrEmpty(_activeProtocolId) && 
                    _protocolMessageSubscribers.TryGetValue(_activeProtocolId, out var subscribers))
                {
                    var parser = _protocolRegistry.GetParser(_activeProtocolId);
                    if (parser != null)
                    {
                        try
                        {
                            var message = parser.Parse(frame.Data);
                            
                            foreach (var subscriber in subscribers)
                            {
                                try
                                {
                                    subscriber(message);
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"ProtocolMessageStream: Protocol message subscriber error: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"ProtocolMessageStream: Parse error: {ex.Message}");
                        }
                    }
                }
            }
        }

        public IReadOnlyList<PhysicalFrame> GetPhysicalFrames(int skip, int take)
        {
            lock (_lock)
            {
                return _physicalFrames
                    .Skip(skip)
                    .Take(take)
                    .ToList();
            }
        }

        public IReadOnlyList<ProtocolMessage> GetProtocolMessages(string? protocolId, int skip, int take)
        {
            if (string.IsNullOrEmpty(protocolId))
            {
                // 如果没有指定协议，使用活动协议
                protocolId = _activeProtocolId;
            }

            if (string.IsNullOrEmpty(protocolId))
            {
                return Array.Empty<ProtocolMessage>();
            }

            var parser = _protocolRegistry.GetParser(protocolId);
            if (parser == null)
            {
                return Array.Empty<ProtocolMessage>();
            }

            lock (_lock)
            {
                var messages = new List<ProtocolMessage>();
                var framesToParse = _physicalFrames.Skip(skip).Take(take);

                foreach (var frame in framesToParse)
                {
                    // 检查缓存
                    if (_frameCache.IsCached(frame.FrameId, protocolId))
                    {
                        // TODO: 从缓存加载已解析的消息
                        // 当前简化版：直接解析
                    }

                    try
                    {
                        var message = parser.Parse(frame.Data);
                        messages.Add(message);

                        // TODO: 缓存解析结果
                    }
                    catch (Exception ex)
                    {
                        // 解析失败，返回错误消息
                        messages.Add(new ProtocolMessage
                        {
                            ProtocolId = protocolId,
                            Content = $"[解析错误] {ex.Message}",
                            RawData = frame.Data.ToArray(),
                            IsValid = false,
                            ErrorMessage = ex.Message
                        });
                    }
                }

                return messages;
            }
        }

        public void SetActiveProtocol(string protocolId)
        {
            lock (_lock)
            {
                _activeProtocolId = protocolId;
            }
        }

        public string? GetActiveProtocol()
        {
            lock (_lock)
            {
                return _activeProtocolId;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _physicalFrames.Clear();
            }
        }

        public IDisposable SubscribeToPhysicalFrames(Action<PhysicalFrame> handler)
        {
            lock (_lock)
            {
                _physicalFrameSubscribers.Add(handler);
            }

            return new Subscription(() =>
            {
                lock (_lock)
                {
                    _physicalFrameSubscribers.Remove(handler);
                }
            });
        }

        public IDisposable SubscribeToProtocolMessages(string protocolId, Action<ProtocolMessage> handler)
        {
            lock (_lock)
            {
                if (!_protocolMessageSubscribers.ContainsKey(protocolId))
                {
                    _protocolMessageSubscribers[protocolId] = new List<Action<ProtocolMessage>>();
                }

                _protocolMessageSubscribers[protocolId].Add(handler);
            }

            return new Subscription(() =>
            {
                lock (_lock)
                {
                    if (_protocolMessageSubscribers.TryGetValue(protocolId, out var subscribers))
                    {
                        subscribers.Remove(handler);
                    }
                }
            });
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
