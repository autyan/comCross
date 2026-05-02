using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class ShutdownHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request)
        => new PluginHostResponse(request.Id, true);
}
