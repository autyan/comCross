using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class DisconnectHandler
{
    public static async Task<PluginHostResponse> HandleAsync(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (state.Instance is not IConnectableBusAdapterPlugin connectable)
        {
            return new PluginHostResponse(request.Id, false, "Plugin does not support disconnect.");
        }

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing disconnect payload.");
        }

        PluginHostDisconnectPayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostDisconnectPayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid disconnect payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid disconnect payload: missing SessionId.");
        }

        if (!state.IsActiveSession(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Session is not active.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandlerDefaults.DefaultOperationTimeout);

            var result = await connectable.DisconnectAsync(
                new PluginDisconnectCommand(payload.SessionId, payload.Reason),
                cts.Token);

            state.EndSession(payload.SessionId);

            var json = JsonSerializer.Serialize(result, jsonOptions);
            var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
            return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: resultPayload);
        }
        catch (OperationCanceledException)
        {
            state.EndSession(payload.SessionId);
            return new PluginHostResponse(request.Id, false, "Timeout.");
        }
        catch (Exception ex)
        {
            state.EndSession(payload.SessionId);
            var restarted = state.RecoverFromStateDamagingFault();
            return new PluginHostResponse(request.Id, false, ex.Message, restarted);
        }
    }
}
