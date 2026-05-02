using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class GetTransmitTargetsHandler
{
    public static PluginHostResponse Handle(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing transmit-target payload.");
        }

        PluginTransmitTargetQuery? query;
        try
        {
            query = request.Payload.Value.Deserialize<PluginTransmitTargetQuery>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid transmit-target payload: {ex.Message}");
        }

        if (query is null || string.IsNullOrWhiteSpace(query.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid transmit-target payload: missing SessionId.");
        }

        if (!state.IsActiveSession(query.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Session is not active.");
        }

        if (state.Instance is not IPluginTransmitTargetProvider provider)
        {
            var empty = new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
            return new PluginHostResponse(
                request.Id,
                true,
                Payload: JsonSerializer.SerializeToElement(empty, jsonOptions));
        }

        try
        {
            var snapshot = provider.GetTransmitTargets(query);
            return new PluginHostResponse(
                request.Id,
                true,
                Payload: JsonSerializer.SerializeToElement(snapshot, jsonOptions));
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }
}
