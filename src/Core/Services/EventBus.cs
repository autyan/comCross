using System.Collections.Concurrent;
using ComCross.Shared.Interfaces;

namespace ComCross.Core.Services;

/// <summary>
/// Thread-safe event bus implementation
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();
    private readonly object _lock = new();

    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent);
        if (_subscribers.TryGetValue(eventType, out var handlers))
        {
            // Create a copy to avoid modification during iteration
            Delegate[] handlersCopy;
            lock (_lock)
            {
                handlersCopy = handlers.ToArray();
            }

            foreach (var handler in handlersCopy)
            {
                try
                {
                    ((Action<TEvent>)handler)(@event);
                }
                catch (Exception ex)
                {
                    // Log error but don't stop other handlers
                    Console.Error.WriteLine($"EventBus: Handler error for {eventType.Name}: {ex.Message}");
                }
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(eventType, out var handlers))
            {
                handlers = new List<Delegate>();
                _subscribers[eventType] = handlers;
            }
            handlers.Add(handler);
        }

        return new Subscription(() => Unsubscribe(eventType, handler));
    }

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        lock (_lock)
        {
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _subscribers.TryRemove(eventType, out _);
                }
            }
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
