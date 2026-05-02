using System;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Plugins.Flow;

public sealed class FlowTool : IExtensionPlugin, IPluginNotificationSubscriber
{
    private string _title = "Flow Tool";

    public PluginMetadata Metadata { get; } = new()
    {
        Id = "serial.flow",
        Name = "Flow Builder",
        Version = "0.3.1",
        Type = PluginType.FlowProcessor
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
            ? "Flow Tool (ZH)"
            : "Flow Tool";
    }
}
