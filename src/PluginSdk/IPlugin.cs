namespace ComCross.PluginSdk;

/// <summary>
/// Base contract shared by all plugins.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Plugin metadata declared by the plugin assembly.
    /// </summary>
    PluginMetadata Metadata { get; }
}
