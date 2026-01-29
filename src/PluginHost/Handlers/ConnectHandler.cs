using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.PluginHost.Handlers;

internal static class ConnectHandler
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
            return new PluginHostResponse(request.Id, false, "Missing connect payload.");
        }

        PluginHostConnectPayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostConnectPayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid connect payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CapabilityId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid connect payload: missing CapabilityId.");
        }

        if (string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid connect payload: missing SessionId.");
        }

        // If capabilities are declared, validate capability id and parameters using lite schema validation.
        if (state.Instance is IPluginCapabilityProvider provider)
        {
            PluginCapabilityDescriptor? capability = null;
            try
            {
                capability = provider.GetCapabilities()?.FirstOrDefault(c => string.Equals(c.Id, payload.CapabilityId, StringComparison.Ordinal));
            }
            catch
            {
                // If capability enumeration fails, treat as invalid and let restart logic handle in the call path.
            }

            if (capability is null)
            {
                return new PluginHostResponse(request.Id, false, $"Unknown capability: {payload.CapabilityId}");
            }

            if (!string.IsNullOrWhiteSpace(capability.JsonSchema))
            {
                if (!JsonSchemaLiteValidator.TryParseSchema(capability.JsonSchema, out var schema, out var parseError))
                {
                    return new PluginHostResponse(request.Id, false, $"Invalid capability schema: {parseError}");
                }

                if (!JsonSchemaLiteValidator.TryValidate(schema, payload.Parameters, out var validateError))
                {
                    return new PluginHostResponse(request.Id, false, $"Parameters validation failed: {validateError}");
                }
            }
        }

        if (state.Instance is not IConnectableBusAdapterPlugin connectable)
        {
            return new PluginHostResponse(request.Id, false, "Plugin does not support connect.");
        }

        if (!state.TryBeginSession(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Another session is already active.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HandlerDefaults.DefaultOperationTimeout);

            var result = await connectable.ConnectAsync(
                new PluginConnectCommand(payload.CapabilityId, payload.Parameters, payload.SessionId),
                cts.Token);

            if (result.Ok)
            {
                // ADR-010 closure: under single-session-per-runtime model, plugin must echo the SessionId.
                if (string.IsNullOrWhiteSpace(result.SessionId))
                {
                    state.EndSession(payload.SessionId);
                    return new PluginHostResponse(request.Id, false, "Protocol violation: plugin did not return SessionId.");
                }

                if (!string.Equals(result.SessionId, payload.SessionId, StringComparison.Ordinal))
                {
                    state.EndSession(payload.SessionId);
                    return new PluginHostResponse(request.Id, false, "Protocol violation: plugin returned mismatched SessionId.");
                }

                state.PublishSessionRegistered(payload.SessionId);
            }
            else
            {
                state.EndSession(payload.SessionId);
            }

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
