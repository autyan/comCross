using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Core-side boundary for plugin-owned session resources.
/// </summary>
public sealed class PluginResourceQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly PluginManagerService _pluginManagerService;
    private readonly PluginHostProtocolService _protocol;

    public PluginResourceQueryService(
        PluginManagerService pluginManagerService,
        PluginHostProtocolService protocol)
    {
        _pluginManagerService = pluginManagerService ?? throw new ArgumentNullException(nameof(pluginManagerService));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
    }

    public async Task<(bool Ok, string? Error, PluginResourceListState? State)> GetResourceListAsync(
        Session ownerSession,
        string resourceKind,
        string resourceId,
        string? viewKind = null,
        string? viewInstanceId = null,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(ownerSession.PluginId))
        {
            return (false, "Missing pluginId.", null);
        }

        if (string.IsNullOrWhiteSpace(ownerSession.CapabilityId))
        {
            return (false, "Missing capabilityId.", null);
        }

        var runtime = _pluginManagerService.GetRuntime(ownerSession.PluginId);
        if (runtime is null)
        {
            return (false, "Plugin runtime not found.", null);
        }

        var (ok, error, snapshot) = await _protocol.GetUiStateAsync(
            runtime,
            ownerSession.CapabilityId,
            sessionId: ownerSession.Id,
            viewKind: viewKind,
            viewInstanceId: viewInstanceId,
            resourceKind: resourceKind,
            resourceId: resourceId,
            timeout: timeout ?? TimeSpan.FromSeconds(1));

        if (!ok || snapshot is null)
        {
            return (false, error, null);
        }

        if (snapshot.State.ValueKind != JsonValueKind.Object)
        {
            return (false, "Resource state must be an object.", null);
        }

        PluginResourceListState? state;
        try
        {
            state = snapshot.State.Deserialize<PluginResourceListState>(JsonOptions);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid resource state: {ex.Message}", null);
        }

        if (state is null)
        {
            return (false, "Invalid resource state.", null);
        }

        if (state.Items is null)
        {
            return (false, "Resource state items missing.", null);
        }

        if (!string.Equals(state.ResourceKind, resourceKind, StringComparison.Ordinal))
        {
            return (false, "Resource state kind mismatch.", null);
        }

        return (true, null, state);
    }
}
