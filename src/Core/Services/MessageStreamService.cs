using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Message stream service for managing log messages per session
/// </summary>
public sealed class MessageStreamService : IMessageStreamService
{
    private readonly ConcurrentDictionary<string, SessionStream> _streams = new();

    public void Append(string sessionId, LogMessage message)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        var stream = _streams.GetOrAdd(sessionId, _ => new SessionStream());
        stream.Append(message);
    }

    public IReadOnlyList<LogMessage> GetMessages(string sessionId, int skip = 0, int take = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        if (!_streams.TryGetValue(sessionId, out var stream))
        {
            return Array.Empty<LogMessage>();
        }

        return stream.GetMessages(skip, take);
    }

    public IReadOnlyList<LogMessage> Search(string sessionId, string query, bool isRegex = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(query);

        if (!_streams.TryGetValue(sessionId, out var stream))
        {
            return Array.Empty<LogMessage>();
        }

        return stream.Search(query, isRegex);
    }

    public void Clear(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        if (_streams.TryGetValue(sessionId, out var stream))
        {
            stream.Clear();
        }
    }

    public IDisposable Subscribe(string sessionId, Action<LogMessage> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(handler);

        var stream = _streams.GetOrAdd(sessionId, _ => new SessionStream());
        return stream.Subscribe(handler);
    }

    private sealed class SessionStream
    {
        private readonly List<LogMessage> _messages = new();
        private readonly List<Action<LogMessage>> _subscribers = new();
        private readonly object _lock = new();

        public void Append(LogMessage message)
        {
            lock (_lock)
            {
                _messages.Add(message);

                // Notify subscribers
                foreach (var subscriber in _subscribers)
                {
                    try
                    {
                        subscriber(message);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"MessageStream: Subscriber error: {ex.Message}");
                    }
                }
            }
        }

        public IReadOnlyList<LogMessage> GetMessages(int skip, int take)
        {
            lock (_lock)
            {
                return _messages
                    .Skip(skip)
                    .Take(take)
                    .ToList();
            }
        }

        public IReadOnlyList<LogMessage> Search(string query, bool isRegex)
        {
            lock (_lock)
            {
                if (isRegex)
                {
                    try
                    {
                        var regex = new Regex(query, RegexOptions.IgnoreCase);
                        return _messages
                            .Where(m => regex.IsMatch(m.Content))
                            .ToList();
                    }
                    catch (ArgumentException)
                    {
                        return Array.Empty<LogMessage>();
                    }
                }
                else
                {
                    return _messages
                        .Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _messages.Clear();
            }
        }

        public IDisposable Subscribe(Action<LogMessage> handler)
        {
            lock (_lock)
            {
                _subscribers.Add(handler);
            }

            return new Subscription(() =>
            {
                lock (_lock)
                {
                    _subscribers.Remove(handler);
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
