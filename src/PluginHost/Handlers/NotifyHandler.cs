using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.PluginHost.Handlers;

internal static class NotifyHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request, HostRuntime state)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (request.Notification is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing notification payload.");
        }

        if (!PluginNotificationTypes.IsKnownGlobal(request.Notification.Type))
        {
            return new PluginHostResponse(request.Id, false, $"Unknown notification type: {request.Notification.Type}");
        }

        if (state.Instance is not IPluginNotificationSubscriber subscriber)
        {
            return new PluginHostResponse(request.Id, true);
        }

        try
        {
            subscriber.OnNotification(request.Notification);
            return new PluginHostResponse(request.Id, true);
        }
        catch (Exception ex)
        {
            // Notifications should not trigger restart: treat as non-state-damaging.
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }
}
