using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class SendDataHandler
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

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing send-data payload.");
        }

        PluginHostSendDataPayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostSendDataPayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid send-data payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid send-data payload: missing SessionId.");
        }

        if (!state.IsActiveSession(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Session is not active.");
        }

        if (state.Instance is not ITransmittableBusAdapterPlugin tx)
        {
            return new PluginHostResponse(request.Id, false, "Plugin does not support TX.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandlerDefaults.DefaultOperationTimeout);

            var result = await tx.SendAsync(
                new PluginSendCommand(payload.SessionId, payload.Data ?? Array.Empty<byte>()),
                cts.Token);

            var json = JsonSerializer.Serialize(result, jsonOptions);
            var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
            return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: resultPayload);
        }
        catch (OperationCanceledException)
        {
            return new PluginHostResponse(request.Id, false, "Timeout.");
        }
        catch (Exception ex)
        {
            var restarted = state.RecoverFromStateDamagingFault();
            return new PluginHostResponse(request.Id, false, ex.Message, restarted);
        }
    }
}
