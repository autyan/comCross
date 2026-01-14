using System;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Plugins.Flow;

public sealed class FlowTool : IPluginNotificationSubscriber
{
    private string _title = "Flow Tool";

    public string Title => _title;

    public void OnNotification(PluginNotification notification)
    {
        if (notification.Type != PluginNotificationTypes.LanguageChanged)
        {
            return;
        }

        var cultureCode = notification.GetData("culture") ?? string.Empty;
        _title = cultureCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "Flow Tool (ZH)"
            : "Flow Tool";
    }
}
