namespace ComCross.PluginHost.Runtime;

internal sealed class SessionManager
{
    private readonly string? _fixedSessionId;
    private readonly object _lock = new();

    private string? _activeSessionId;
    private readonly HashSet<string> _activeSessions = new(StringComparer.Ordinal);

    public SessionManager(string? fixedSessionId)
    {
        _fixedSessionId = string.IsNullOrWhiteSpace(fixedSessionId) ? null : fixedSessionId;
    }

    public bool SupportsMultiSession { get; set; }

    public bool TryBeginSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (_fixedSessionId is not null && !string.Equals(_fixedSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_lock)
        {
            if (SupportsMultiSession)
            {
                _activeSessions.Add(sessionId);
                return true;
            }

            if (string.IsNullOrWhiteSpace(_activeSessionId))
            {
                _activeSessionId = sessionId;
                return true;
            }

            return string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal);
        }
    }

    public bool IsActiveSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (_fixedSessionId is not null && !string.Equals(_fixedSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_lock)
        {
            if (SupportsMultiSession)
            {
                return _activeSessions.Contains(sessionId);
            }

            return !string.IsNullOrWhiteSpace(_activeSessionId)
                && string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal);
        }
    }

    public void EndSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_fixedSessionId is not null && !string.Equals(_fixedSessionId, sessionId, StringComparison.Ordinal))
        {
            return;
        }

        lock (_lock)
        {
            if (SupportsMultiSession)
            {
                _activeSessions.Remove(sessionId);
                return;
            }

            if (string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
            {
                _activeSessionId = null;
            }
        }
    }

    public IReadOnlyList<string> SnapshotActiveSessions()
    {
        lock (_lock)
        {
            if (SupportsMultiSession)
            {
                return _activeSessions.ToArray();
            }

            return string.IsNullOrWhiteSpace(_activeSessionId) ? Array.Empty<string>() : new[] { _activeSessionId };
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _activeSessionId = null;
            _activeSessions.Clear();
        }
    }
}
