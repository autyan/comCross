using System.Text.Json;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Routes host events from bus/session/extension planes into core-level events and UI state refreshes.
/// </summary>
public sealed class PluginHostEventRouterService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IEventBus _eventBus;
    private readonly PluginUiStateManager _uiStateManager;
    private readonly IPluginUiStateFetcher _uiStateFetcher;
    private readonly IExtensionActionExecutor _extensionActionExecutor;
    private readonly ILogger<PluginHostEventRouterService> _logger;

    public PluginHostEventRouterService(
        IEventBus eventBus,
        PluginUiStateManager uiStateManager,
        IPluginUiStateFetcher uiStateFetcher,
        IExtensionActionExecutor extensionActionExecutor,
        ILogger<PluginHostEventRouterService> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _uiStateManager = uiStateManager ?? throw new ArgumentNullException(nameof(uiStateManager));
        _uiStateFetcher = uiStateFetcher ?? throw new ArgumentNullException(nameof(uiStateFetcher));
        _extensionActionExecutor = extensionActionExecutor ?? throw new ArgumentNullException(nameof(extensionActionExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RouteAsync(PluginRuntime runtime, PluginHostEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(evt);

        if (evt.Payload is null)
        {
            return;
        }

        if (string.Equals(evt.Type, PluginHostEventTypes.UiStateInvalidated, StringComparison.Ordinal))
        {
            await HandleUiStateInvalidatedAsync(runtime, evt.Payload.Value, cancellationToken);
            return;
        }

        if (string.Equals(evt.Type, PluginHostEventTypes.ExtensionActionRequested, StringComparison.Ordinal))
        {
            await HandleExtensionActionRequestedAsync(runtime, evt.Payload.Value, cancellationToken);
        }
    }

    private async Task HandleUiStateInvalidatedAsync(
        PluginRuntime runtime,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        PluginHostUiStateInvalidatedEvent? invalidated;
        try
        {
            invalidated = payload.Deserialize<PluginHostUiStateInvalidatedEvent>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse UiStateInvalidated from {PluginId}", runtime.Info.Manifest.Id);
            return;
        }

        if (invalidated is null || string.IsNullOrWhiteSpace(invalidated.CapabilityId))
        {
            return;
        }

        var pluginId = string.IsNullOrWhiteSpace(invalidated.PluginId)
            ? runtime.Info.Manifest.Id
            : invalidated.PluginId;

        _eventBus.Publish(new PluginUiStateInvalidatedCoreEvent(
            pluginId,
            invalidated.CapabilityId,
            invalidated.SessionId,
            invalidated.ViewKind,
            invalidated.ViewInstanceId,
            invalidated.Reason));

        var (ok, error, snapshot) = await _uiStateFetcher.GetUiStateAsync(runtime, invalidated, cancellationToken);
        if (!ok || snapshot is null)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogDebug(
                    "UI state refresh failed after invalidation: PluginId={PluginId}, CapabilityId={CapabilityId}, SessionId={SessionId}, Error={Error}",
                    pluginId,
                    invalidated.CapabilityId,
                    invalidated.SessionId ?? "-",
                    error);
            }

            return;
        }

        if (snapshot.State.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            _logger.LogDebug(
                "UI state snapshot ignored because it is not an object: PluginId={PluginId}, CapabilityId={CapabilityId}",
                pluginId,
                invalidated.CapabilityId);
            return;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in snapshot.State.EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        var viewScope = PluginUiViewScope.From(invalidated.ViewKind, invalidated.ViewInstanceId);
        _uiStateManager.SetStateSnapshot(viewScope, invalidated.SessionId, values);
    }

    private async Task HandleExtensionActionRequestedAsync(
        PluginRuntime runtime,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        PluginHostExtensionActionRequestEvent? request;
        try
        {
            request = payload.Deserialize<PluginHostExtensionActionRequestEvent>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse extension action request from {PluginId}", runtime.Info.Manifest.Id);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Action))
        {
            return;
        }

        await _extensionActionExecutor.ExecuteAsync(runtime, request, cancellationToken);
    }
}
