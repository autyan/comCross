using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.Shell.Services;

public sealed class PluginManagementService
{
    private readonly PluginManagerService _pluginManagerService;
    private readonly PluginHostProtocolService _protocolService;
    private readonly SettingsService _settingsService;

    public PluginManagementService(
        PluginManagerService pluginManagerService,
        PluginHostProtocolService protocolService,
        SettingsService settingsService)
    {
        _pluginManagerService = pluginManagerService;
        _protocolService = protocolService;
        _settingsService = settingsService;
    }

    public bool IsPluginEnabled(string pluginId)
        => _settingsService.Current.Plugins.Enabled.TryGetValue(pluginId, out var enabled) ? enabled : true;

    public IReadOnlyList<PluginRuntime> GetAllRuntimes() => _pluginManagerService.GetAllRuntimes();

    public PluginRuntime? GetRuntime(string pluginId) => _pluginManagerService.GetRuntime(pluginId);

    public Task<PluginToggleResult> SetPluginEnabledAsync(string pluginId, bool enabled)
        => _pluginManagerService.SetPluginEnabledAsync(pluginId, enabled);

    public Task ShutdownAsync() => _pluginManagerService.ShutdownAsync();

    public Task<PluginConnectResult> ConnectAsync(
        PluginRuntime runtime,
        string capabilityId,
        JsonElement parameters,
        TimeSpan timeout)
        => _protocolService.ConnectAsync(runtime, capabilityId, parameters, timeout);

    public Task<PluginCommandResult> DisconnectAsync(
        PluginRuntime runtime,
        string? sessionId,
        string reason,
        TimeSpan timeout)
        => _protocolService.DisconnectAsync(runtime, sessionId, reason, timeout);

    public Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
        PluginRuntime runtime,
        string capabilityId,
        string? sessionId,
        string? viewKind,
        string? viewInstanceId,
        string? resourceKind,
        string? resourceId,
        TimeSpan timeout)
        => _protocolService.GetUiStateAsync(
            runtime,
            capabilityId,
            sessionId,
            viewKind,
            viewInstanceId,
            resourceKind,
            resourceId,
            timeout);

    public Task<(bool Ok, string? Error, PluginCommandResult? Result)> ExecuteActionAsync(
        PluginRuntime runtime,
        string actionName,
        string? sessionId,
        JsonElement? parameters,
        TimeSpan timeout)
        => _protocolService.ExecuteActionAsync(runtime, actionName, sessionId, parameters, timeout);

    public Task NotifyPluginsAsync(PluginNotification notification, Action<PluginRuntime, Exception, bool>? onError = null)
        => _pluginManagerService.NotifyPluginsAsync(notification, onError);
}
