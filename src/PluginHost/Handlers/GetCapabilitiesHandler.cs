using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class GetCapabilitiesHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request, HostRuntime state, JsonSerializerOptions jsonOptions)
    {
        if (!state.IsLoaded)
        {
            return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
        }

        if (state.Instance is not IPluginCapabilityProvider provider)
        {
            // Capability declaration is optional; treat as empty.
            var empty = JsonDocument.Parse("[]").RootElement.Clone();
            return new PluginHostResponse(request.Id, true, Payload: empty);
        }

        try
        {
            var capabilities = provider.GetCapabilities() ?? Array.Empty<PluginCapabilityDescriptor>();
            var json = JsonSerializer.Serialize(capabilities, jsonOptions);
            var payload = JsonDocument.Parse(json).RootElement.Clone();
            return new PluginHostResponse(request.Id, true, Payload: payload);
        }
        catch (Exception ex)
        {
            // Read-only query; do not restart.
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }
}
