using ComCross.Shared.Models;
using ComCross.PluginHost.Runtime;

namespace ComCross.PluginHost.Handlers;

internal static class PingHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request, HostRuntime state)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        return new PluginHostResponse(request.Id, true);
    }
}
