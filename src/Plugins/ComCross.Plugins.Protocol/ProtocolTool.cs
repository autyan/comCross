using System;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Plugins.Protocol;

public sealed class ProtocolTool : IExtensionPlugin, IPluginNotificationSubscriber
{
    private string _title = "Protocol Tool";

    public PluginMetadata Metadata { get; } = new()
    {
        Id = "serial.protocol",
        Name = "Protocol Helper",
        Version = "0.3.1",
        Type = PluginType.Extension
    };

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
