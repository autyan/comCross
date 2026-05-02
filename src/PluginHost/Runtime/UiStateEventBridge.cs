using ComCross.PluginSdk;

namespace ComCross.PluginHost.Runtime;

internal sealed class UiStateEventBridge
{
    private readonly Action<PluginUiStateInvalidatedEvent> _defaultSink;

    private Action<PluginUiStateInvalidatedEvent>? _sink;
    private IPluginUiStateEventSource? _source;

    public UiStateEventBridge(Action<PluginUiStateInvalidatedEvent> defaultSink)
    {
        _defaultSink = defaultSink;
        _sink = defaultSink;
    }

    public void SetSink(Action<PluginUiStateInvalidatedEvent> sink)
    {
        _sink = sink ?? _defaultSink;
    }

    public void BindTo(object? pluginInstance)
    {
        if (_source is not null)
        {
            _source.UiStateInvalidated -= OnUiStateInvalidated;
        }

        _source = pluginInstance as IPluginUiStateEventSource;
        if (_source is not null)
        {
            _source.UiStateInvalidated += OnUiStateInvalidated;
        }
    }

    public void Unbind()
    {
        if (_source is not null)
        {
            _source.UiStateInvalidated -= OnUiStateInvalidated;
        }

        _source = null;
    }

    private void OnUiStateInvalidated(object? sender, PluginUiStateInvalidatedEvent evt)
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
