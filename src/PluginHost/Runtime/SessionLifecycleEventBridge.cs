using ComCross.PluginSdk;

namespace ComCross.PluginHost.Runtime;

internal sealed class SessionLifecycleEventBridge
{
    private readonly Action<PluginSessionClosedEvent> _defaultSink;

    private Action<PluginSessionClosedEvent>? _sink;
    private IPluginSessionLifecycleEventSource? _source;

    public SessionLifecycleEventBridge(Action<PluginSessionClosedEvent> defaultSink)
    {
        _defaultSink = defaultSink;
        _sink = defaultSink;
    }

    public void SetSink(Action<PluginSessionClosedEvent> sink)
    {
        _sink = sink ?? _defaultSink;
    }

    public void BindTo(object? pluginInstance)
    {
        if (_source is not null)
        {
            _source.SessionClosed -= OnSessionClosed;
        }

        _source = pluginInstance as IPluginSessionLifecycleEventSource;
        if (_source is not null)
        {
            _source.SessionClosed += OnSessionClosed;
        }
    }

    private void OnSessionClosed(object? sender, PluginSessionClosedEvent evt)
    {
        try
        {
            _sink?.Invoke(evt);
        }
        catch
        {
        }
    }
}
