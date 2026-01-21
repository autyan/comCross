using System;
using System.Text.Json;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Shell.Services;
using ComCross.Shell.ViewModels;
using Microsoft.Extensions.Logging;

namespace ComCross.Shell.Plugins.UI;

public class ShellPluginCommunicationLink : IPluginCommunicationLink
{
    private readonly PluginManagerViewModel _pluginManager;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly SerialPortsHostService _serialPorts;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ShellPluginCommunicationLink> _logger;

    public ShellPluginCommunicationLink(
        PluginManagerViewModel pluginManager, 
        ICapabilityDispatcher dispatcher,
        SerialPortsHostService serialPorts,
        ILocalizationService localization,
        ILogger<ShellPluginCommunicationLink> logger)
    {
        _pluginManager = pluginManager;
        _dispatcher = dispatcher;
        _serialPorts = serialPorts;
        _localization = localization;
        _logger = logger;
    }

    public async Task SendActionAsync(string pluginId, string? sessionId, string actionName, object? parameters)
    {
        _logger.LogInformation("Sending action {Action} to plugin {PluginId} (Session: {SessionId})", actionName, pluginId, sessionId ?? "null");

        // Host-only actions (do not go through Core dispatcher)
        if (string.Equals(actionName, SerialPortsHostService.RefreshPortsHostAction, StringComparison.Ordinal))
        {
            await _serialPorts.RefreshPortsAsync(pluginId, sessionId);
            return;
        }
        
        try
        {
            // Pass to Core Dispatcher
            await _dispatcher.DispatchAsync(pluginId, sessionId, actionName, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action {Action} failed for plugin {PluginId} (Session: {SessionId})", actionName, pluginId, sessionId ?? "null");
            var title = _localization.GetString("connection.error.failed");
            await MessageBoxService.ShowErrorAsync(title, ex.Message);
        }
    }
}
