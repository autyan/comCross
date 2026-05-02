using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class GetUiStateHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request, HostRuntime state, JsonSerializerOptions jsonOptions)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing ui-state payload.");
        }

        PluginHostGetUiStatePayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostGetUiStatePayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid ui-state payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CapabilityId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid ui-state payload: missing CapabilityId.");
        }

        // ADR-010 closure: sessionless is represented by null only.
        // Empty/whitespace session ids are invalid and must not bypass gating.
        if (payload.SessionId is not null && string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid ui-state payload: invalid SessionId.");
        }

        if (state.Instance is not IPluginUiStateProvider provider)
        {
            return new PluginHostResponse(request.Id, false, "Plugin does not support ui-state.");
        }

        if (payload.SessionId is not null && !state.IsActiveSession(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Session is not active.");
        }

        try
        {
            var uiState = provider.GetUiState(new PluginUiStateQuery(
                payload.CapabilityId,
                payload.SessionId,
                payload.ViewKind,
                payload.ViewInstanceId,
                payload.ResourceKind,
                payload.ResourceId,
                payload.Settings));

            var json = JsonSerializer.Serialize(uiState, jsonOptions);
            var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
            return new PluginHostResponse(request.Id, true, Payload: resultPayload);
        }
        catch (Exception ex)
        {
            // Read-only query; do not restart.
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }
}
