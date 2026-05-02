using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class ExecuteActionHandler
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

        if (state.Instance is not IPluginActionHandler actionHandler)
        {
            return new PluginHostResponse(request.Id, false, "Plugin does not support custom actions.");
        }

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing execute-action payload.");
        }

        PluginHostExecuteActionPayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostExecuteActionPayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid execute-action payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.ActionName))
        {
            return new PluginHostResponse(request.Id, false, "Invalid execute-action payload: missing ActionName.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandlerDefaults.DefaultOperationTimeout);

            var result = await actionHandler.ExecuteActionAsync(
                new PluginActionCommand(
                    payload.ActionName,
                    request.SessionId,
                    payload.Parameters ?? JsonSerializer.SerializeToElement(new { })),
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
