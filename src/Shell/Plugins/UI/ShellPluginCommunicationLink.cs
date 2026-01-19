using System;
using System.Text.Json;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Models;
using ComCross.Shell.ViewModels;
using Microsoft.Extensions.Logging;

namespace ComCross.Shell.Plugins.UI;

public class ShellPluginCommunicationLink : IPluginCommunicationLink
{
    private readonly PluginManagerViewModel _pluginManager;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly ILogger<ShellPluginCommunicationLink> _logger;

    public ShellPluginCommunicationLink(
        PluginManagerViewModel pluginManager, 
        ICapabilityDispatcher dispatcher,
        ILogger<ShellPluginCommunicationLink> logger)
    {
        _pluginManager = pluginManager;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task SendActionAsync(string pluginId, string? sessionId, string actionName, object? parameters)
    {
        _logger.LogInformation("Sending action {Action} to plugin {PluginId} (Session: {SessionId})", actionName, pluginId, sessionId ?? "null");
        
        // Pass to Core Dispatcher
        await _dispatcher.DispatchAsync(pluginId, sessionId, actionName, parameters);
    }
}
