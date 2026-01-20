using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// 能力分发器接口，负责将来自 UI 的 Action 路由到正确的处理器
/// </summary>
public interface ICapabilityDispatcher
{
    Task DispatchAsync(string? pluginId, string? sessionId, string actionName, object? parameters);
}

/// <summary>
/// 核心业务分发实现
/// 它是 Shell 与插件/驱动逻辑之间的断路器
/// </summary>
public class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly DeviceService _deviceService;
    private readonly PluginUiStateManager _stateManager;
    private readonly PluginManagerService _pluginManager;
    private readonly IEventBus _eventBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CapabilityDispatcher> _logger;

    public CapabilityDispatcher(
        DeviceService deviceService,
        PluginUiStateManager stateManager,
        PluginManagerService pluginManager,
        IEventBus eventBus,
        IServiceProvider serviceProvider,
        ILogger<CapabilityDispatcher> logger)
    {
        _deviceService = deviceService;
        _stateManager = stateManager;
        _pluginManager = pluginManager;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task DispatchAsync(string? pluginId, string? sessionId, string actionName, object? parameters)
    {
        _logger.LogInformation("Dispatching action {Action} for Plugin:{PluginId} Session:{SessionId}", 
            actionName, pluginId ?? "(none)", sessionId ?? "(none)");

        // 1. Resolve pluginId if not provided but we have a session
        var resolvedPluginId = pluginId;
        if (string.IsNullOrEmpty(resolvedPluginId) && !string.IsNullOrEmpty(sessionId))
        {
            var session = _deviceService.GetSession(sessionId);
            resolvedPluginId = session?.PluginId;
            _logger.LogDebug("Resolved PluginId {PluginId} from SessionId {SessionId}", resolvedPluginId, sessionId);
        }

        if (string.IsNullOrEmpty(resolvedPluginId))
        {
            _logger.LogError("Action {Action} failed: No PluginId or SessionId could be resolved.", actionName);
            throw new ArgumentException($"Action '{actionName}' requires either pluginId or sessionId.");
        }

        // 2. Handle Action by routing to appropriate capability provider (Built-in or External)
        await HandleActionInternalAsync(resolvedPluginId, sessionId, actionName, parameters);
    }

    private async Task HandleActionInternalAsync(string pluginId, string? sessionId, string actionName, object? parameters)
    {
        var runtime = _pluginManager.GetRuntime(pluginId);
        if (runtime == null || runtime.Client == null)
        {
            _logger.LogError("Capability Provider for '{PluginId}' is not available.", pluginId);
            throw new InvalidOperationException($"Capability provider '{pluginId}' is not running or available.");
        }

        // 1. Lifecycle Actions (Standardized in PluginHostMessageTypes)
        // These are actions that require Core-side orchestration (Session management, Shared Memory, etc.)
        if (string.Equals(actionName, PluginHostMessageTypes.Connect, StringComparison.Ordinal))
        {
            var targetSessionId = sessionId ?? $"session-{Guid.NewGuid():N}";
            var uiParams = ExtractParameters(parameters) ?? new Dictionary<string, object>();
            
            // Extract capabilityId from parameters or default to pluginId
            string capabilityId = pluginId; 
            if (uiParams.TryGetValue("capabilityId", out var capIdObj)) capabilityId = capIdObj.ToString()!;

            // Convert back to JsonElement for DeviceService
            var parametersJson = JsonSerializer.Serialize(uiParams);
            var parametersElement = JsonSerializer.Deserialize<JsonElement>(parametersJson);

            // Connect via DeviceService which handles SHM and Plugin Host 'Connect' command
            await _deviceService.ConnectAsync(pluginId, capabilityId, targetSessionId, capabilityId, parametersElement);
            return;
        }

        if (string.Equals(actionName, PluginHostMessageTypes.Disconnect, StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(sessionId)) 
            {
                _logger.LogWarning("Disconnect action requested without sessionId.");
                return;
            }
            await _deviceService.DisconnectAsync(sessionId);
            return;
        }

        // 2. Passthrough Actions (Non-standard or Plugin-specific)
        // These are forwarded directly to the plugin host for processing.
        _logger.LogDebug("Forwarding custom action '{Action}' to plugin '{PluginId}'", actionName, pluginId);
        
        var payloadElement = parameters == null ? null : (JsonElement?)JsonSerializer.SerializeToElement(parameters);
        var request = new PluginHostRequest(
            Guid.NewGuid().ToString("N"),
            actionName,
            sessionId,
            null,
            payloadElement
        );

        var response = await runtime.Client.SendAsync(request, TimeSpan.FromSeconds(10));
        if (response != null && !response.Ok)
        {
            _logger.LogError("Plugin action '{Action}' failed: {Error}", actionName, response.Error);
            throw new Exception($"Action '{actionName}' failed: {response.Error}");
        }
    }

    private IDictionary<string, object>? ExtractParameters(object? parameters)
    {
        if (parameters == null) return null;
        if (parameters is IDictionary<string, object> dict) return dict;
        if (parameters is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());
            }
        }
        
        try 
        {
            var json = JsonSerializer.Serialize(parameters);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch 
        {
            return null;
        }
    }
}
