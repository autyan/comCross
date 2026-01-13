namespace ComCross.Shared.Interfaces;

/// <summary>
/// Event bus for decoupled communication between modules
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all subscribers
    /// </summary>
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    /// <summary>
    /// Subscribes to events of a specific type
    /// </summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
