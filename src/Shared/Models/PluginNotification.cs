using System.Collections.Generic;

namespace ComCross.Shared.Models;

public sealed record PluginNotification(string Type, IReadOnlyDictionary<string, string?>? Data = null)
{
    public string? GetData(string key)
    {
        return Data != null && Data.TryGetValue(key, out var value) ? value : null;
    }

    public static PluginNotification LanguageChanged(string cultureCode)
    {
        return new PluginNotification(
            PluginNotificationTypes.LanguageChanged,
            new Dictionary<string, string?> { ["culture"] = cultureCode });
    }
}

public static class PluginNotificationTypes
{
    public const string LanguageChanged = "plugin.language.changed";
}
