using ComCross.Shared.Models;

namespace ComCross.Shared.Services;

public interface IPluginNotificationSubscriber
{
    void OnNotification(PluginNotification notification);
}
