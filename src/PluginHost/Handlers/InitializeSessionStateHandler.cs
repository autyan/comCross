using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class InitializeSessionStateHandler
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
            return new PluginHostResponse(request.Id, false, "Missing session initialization payload.");
        }

        PluginSessionStateInitializationContext? context;
        try
        {
            context = request.Payload.Value.Deserialize<PluginSessionStateInitializationContext>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid session initialization payload: {ex.Message}");
        }

        if (context is null || string.IsNullOrWhiteSpace(context.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid session initialization payload: missing SessionId.");
        }

        if (state.Instance is not IPluginSessionStateInitializer initializer)
        {
            var empty = new PluginSessionStateInitializationResult(true);
            var emptyPayload = JsonSerializer.SerializeToElement(empty, jsonOptions);
            return new PluginHostResponse(request.Id, true, Payload: emptyPayload);
        }

        try
        {
            var result = await initializer.InitializeSessionStateAsync(context, cancellationToken);
            var payload = JsonSerializer.SerializeToElement(result, jsonOptions);
            return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: payload);
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
