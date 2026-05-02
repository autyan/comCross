using ComCross.PluginSdk;

namespace ComCross.Core.Services;

public static class PluginPlaneClassifier
{
    public static bool TryClassify(PluginManifest manifest, out PluginPlane plane, out string? error)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!manifest.PluginType.HasValue)
        {
            plane = default;
            error = $"Plugin '{manifest.Id}' is missing required manifest field 'pluginType'.";
            return false;
        }

        plane = manifest.PluginType.Value switch
        {
            PluginType.BusAdapter => PluginPlane.Bus,
            PluginType.FlowProcessor => PluginPlane.Extension,
            PluginType.Statistics => PluginPlane.Extension,
            PluginType.UIExtension => PluginPlane.Extension,
            PluginType.Extension => PluginPlane.Extension,
            _ => default
        };

        error = manifest.PluginType.Value switch
        {
            PluginType.BusAdapter => null,
            PluginType.FlowProcessor => null,
            PluginType.Statistics => null,
            PluginType.UIExtension => null,
            PluginType.Extension => null,
            _ => $"Plugin '{manifest.Id}' has unsupported pluginType '{manifest.PluginType.Value}'."
        };

        return error is null;
    }
}
