using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.PluginHost.Runtime;

internal sealed class HostRuntime
{
    private readonly PluginLifecycleManager _lifecycle;
    private readonly SessionManager _sessions;
    private readonly SharedMemoryWriterManager _writers;
    private readonly UiStateEventBridge _uiBridge;

    private Action<string, string>? _sessionRegisteredSink;

    public HostRuntime(string entryPoint, string pluginPath, string? fixedSessionId)
    {
        _lifecycle = new PluginLifecycleManager(entryPoint, pluginPath);
        _sessions = new SessionManager(fixedSessionId);
        _writers = new SharedMemoryWriterManager();
        _uiBridge = new UiStateEventBridge(_ => { });
    }

    public object? Instance => _lifecycle.Instance;
    public bool IsLoaded => _lifecycle.IsLoaded;
    public string? LoadError => _lifecycle.LoadError;

    public string? HostToken { get; private set; }

    public void SetHostToken(string? token)
    {
        HostToken = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public void SetSessionRegisteredSink(Action<string, string> sink)
    {
        _sessionRegisteredSink = sink;
    }

    public void PublishSessionRegistered(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(HostToken))
            {
                return;
            }

            _sessionRegisteredSink?.Invoke(HostToken, sessionId);
        }
        catch
        {
        }
    }

    public void SetUiStateEventSink(Action<PluginUiStateInvalidatedEvent> sink)
    {
        _uiBridge.SetSink(sink);
        _uiBridge.BindTo(Instance);
    }

    public void TryLoadPlugin()
    {
        _lifecycle.LoadPlugin();
        _sessions.SupportsMultiSession = Instance is IMultiSessionDevicePlugin;
        _writers.Reset(Instance);
        _uiBridge.BindTo(Instance);
    }

    public bool TryRestart()
    {
        ResetState();
        var ok = _lifecycle.Restart();
        _sessions.SupportsMultiSession = Instance is IMultiSessionDevicePlugin;
        _uiBridge.BindTo(Instance);
        return ok;
    }

    public void ResetState()
    {
        // End sessions first so per-session writers can be cleared.
        var active = _sessions.SnapshotActiveSessions();
        foreach (var sessionId in active)
        {
            EndSession(sessionId);
        }

        _writers.Reset(Instance);
        _sessions.Reset();
    }

    public bool TryBeginSession(string sessionId) => _sessions.TryBeginSession(sessionId);

    public bool IsActiveSession(string sessionId) => _sessions.IsActiveSession(sessionId);

    public void EndSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        // Clear per-session writer on multi-session plugin.
        _writers.ClearWriterForSession(Instance, sessionId);
        _sessions.EndSession(sessionId);

        // Single-session: if session ended, reset writers.
        if (!_sessions.SupportsMultiSession)
        {
            _writers.Reset(Instance);
        }
    }

    public bool TryApplySharedMemoryWriter(string sessionId, SharedMemorySegmentDescriptor descriptor)
    {
        if (Instance is null)
        {
            return false;
        }

        return _writers.TryApplyWriter(Instance, sessionId, descriptor);
    }

    public bool RecoverFromStateDamagingFault()
    {
        // For state-damaging faults, do a full reset and restart.
        return TryRestart();
    }
}
