using System;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Plugins.Protocol;

public sealed class ProtocolTool : IPluginNotificationSubscriber
{
    private string _title = "Protocol Tool";

    public string Title => _title;

    public void OnNotification(PluginNotification notification)
    {
        if (notification.Type != PluginNotificationTypes.LanguageChanged)
        {
            return;
        }

        var cultureCode = notification.GetData("culture") ?? string.Empty;
        _title = cultureCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "Protocol Tool (ZH)"
            : "Protocol Tool";
    }
}
