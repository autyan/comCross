namespace ComCross.PluginHost.Logging;

public static class PluginHostLogKey
{
    public static string Build(int processId, string pluginId, string role)
    {
        var pidPart = processId <= 0 ? "pid-unknown" : $"pid-{processId:D6}";
        var pluginPart = string.IsNullOrWhiteSpace(pluginId) ? "plugin-unknown" : $"plugin-{pluginId.Trim()}";
        var rolePart = string.IsNullOrWhiteSpace(role) ? "role-unknown" : $"role-{role.Trim()}";
        return $"{pidPart}_{pluginPart}_{rolePart}";
    }
}
