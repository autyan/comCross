namespace ComCross.PluginSdk;

/// <summary>
/// Optional contract for extension plugins that consume frame batches from Core.
/// </summary>
public interface IExtensionFrameBatchConsumer
{
    void OnFrameBatch(IReadOnlyList<ExtensionFrame> frames);
}
