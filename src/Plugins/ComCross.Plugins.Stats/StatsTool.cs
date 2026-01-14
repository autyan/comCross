using System;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Plugins.Stats;

public sealed class StatsTool : IPluginNotificationSubscriber
{
    private string _title = "Stats Tool";

    public string Title => _title;

    public void OnNotification(PluginNotification notification)
    {
        if (notification.Type != PluginNotificationTypes.LanguageChanged)
        {
            return;
        }

        var cultureCode = notification.GetData("culture") ?? string.Empty;
        _title = cultureCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "Stats Tool (ZH)"
            : "Stats Tool";
    }
}
