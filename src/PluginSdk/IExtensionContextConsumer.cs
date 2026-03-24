namespace ComCross.PluginSdk;

/// <summary>
/// Optional contract for extension plugins that consume workspace/session/settings snapshots.
/// </summary>
public interface IExtensionContextConsumer
{
    void OnContextSnapshot(ExtensionContextSnapshot snapshot);
}
