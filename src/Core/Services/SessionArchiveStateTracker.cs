using System.Collections.Concurrent;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class SessionArchiveStateTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, SessionArchiveState> _states = new(StringComparer.Ordinal);
    private readonly IDisposable _createdSubscription;
    private readonly IDisposable _updatedSubscription;
    private readonly IDisposable _deletedSubscription;

    public SessionArchiveStateTracker(IEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        _createdSubscription = eventBus.Subscribe<SessionCreatedEvent>(evt => Track(evt.Session));
        _updatedSubscription = eventBus.Subscribe<SessionUpdatedEvent>(evt => Track(evt.Session));
        _deletedSubscription = eventBus.Subscribe<SessionDeletedEvent>(evt => _states.TryRemove(evt.SessionId, out _));
    }

    public bool IsEnabled(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId)
            && _states.TryGetValue(sessionId, out var state)
            && state == SessionArchiveState.Enabled;

    public bool CanReadArchive(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId)
            && _states.TryGetValue(sessionId, out var state)
            && state is SessionArchiveState.Enabled or SessionArchiveState.Stopped or SessionArchiveState.Error;

    private void Track(Session? session)
    {
        if (session?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        _states[sessionId] = session.ArchiveState;
    }

    public void Dispose()
    {
        _createdSubscription.Dispose();
        _updatedSubscription.Dispose();
        _deletedSubscription.Dispose();
    }
}
