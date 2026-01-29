using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class SetBackpressureHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request, HostRuntime state, JsonSerializerOptions jsonOptions)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing set-backpressure payload.");
        }

        PluginHostSetBackpressurePayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostSetBackpressurePayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid set-backpressure payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid set-backpressure payload: missing SessionId.");
        }

        if (!state.IsActiveSession(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Session is not active.");
        }

        if (state.Instance is not IDevicePlugin plugin)
        {
            return new PluginHostResponse(request.Id, false, "Plugin does not support backpressure.");
        }

        try
        {
            plugin.SetBackpressureLevel(payload.Level);
            return new PluginHostResponse(request.Id, true);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }
}
