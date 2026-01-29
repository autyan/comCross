using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class ApplySharedMemorySegmentHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request, HostRuntime state, JsonSerializerOptions jsonOptions)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing apply-shared-memory-segment payload.");
        }

        PluginHostApplySharedMemorySegmentPayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostApplySharedMemorySegmentPayload>(jsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid apply-shared-memory-segment payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId) || payload.Descriptor is null)
        {
            return new PluginHostResponse(request.Id, false, "Invalid apply-shared-memory-segment payload.");
        }

        if (!state.IsActiveSession(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Session is not active.");
        }

        try
        {
            var ok = state.TryApplySharedMemoryWriter(payload.SessionId, payload.Descriptor);
            return ok
                ? new PluginHostResponse(request.Id, true)
                : new PluginHostResponse(request.Id, false, "Failed to apply shared memory segment.");
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }
}
