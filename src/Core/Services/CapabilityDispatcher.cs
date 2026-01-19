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
    Task DispatchAsync(string pluginId, string? sessionId, string actionName, object? parameters);
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

    public async Task DispatchAsync(string pluginId, string? sessionId, string actionName, object? parameters)
    {
        _logger.LogInformation("Dispatching action {Action} for {PluginId}", actionName, pluginId);

        // 1. 处理内置能力 (System)
        if (pluginId.StartsWith("system.", StringComparison.Ordinal))
        {
            await HandleSystemActionAsync(pluginId, sessionId, actionName, parameters);
            return;
        }

        // 2. 处理插件能力
        await HandlePluginActionAsync(pluginId, sessionId, actionName, parameters);
    }

    private async Task HandleSystemActionAsync(string pluginId, string? sessionId, string actionName, object? parameters)
    {
        if (string.Equals(pluginId, "system.serial", StringComparison.Ordinal))
        {
            await HandleSerialActionAsync(sessionId, actionName, parameters);
        }
        else
        {
            throw new NotSupportedException($"System capability '{pluginId}' is not implemented.");
        }
    }

    private async Task HandleSerialActionAsync(string? sessionId, string actionName, object? parameters)
    {
        if (string.Equals(actionName, "refresh", StringComparison.Ordinal))
        {
            var devices = await _deviceService.ListDevicesAsync();
            var ports = devices.Select(d => d.Port).ToList();
            _stateManager.UpdateState(sessionId, "system.serial.ports", ports);
        }
        else if (string.Equals(actionName, "connect", StringComparison.Ordinal))
        {
            // 解析 UI 参数
            var uiParams = ExtractParameters(parameters);
            if (uiParams == null || !uiParams.TryGetValue("port", out var portObj) || portObj == null)
            {
                throw new ArgumentException("Serial port is required.");
            }

            var port = portObj.ToString()!;
            var settings = new SerialSettings();
            
            if (uiParams.TryGetValue("baudRate", out var br) && int.TryParse(br?.ToString(), out var b)) settings.BaudRate = b;
            if (uiParams.TryGetValue("dataBits", out var db) && int.TryParse(db?.ToString(), out var d)) settings.DataBits = d;
            
            // 执行连接 (Core 层逻辑)
            var targetSessionId = sessionId ?? $"session-{Guid.NewGuid():N}";
            await _deviceService.ConnectAsync(targetSessionId, port, port, settings);
        }
        else if (string.Equals(actionName, "disconnect", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            await _deviceService.DisconnectAsync(sessionId);
        }
    }

    private async Task HandlePluginActionAsync(string pluginId, string? sessionId, string actionName, object? parameters)
    {
        var runtime = _pluginManager.GetRuntime(pluginId);
        if (runtime == null || runtime.Client == null)
        {
            throw new InvalidOperationException($"Plugin '{pluginId}' is not running or available.");
        }

        // 修正会话连接逻辑
        var targetSessionId = sessionId;
        if (string.Equals(actionName, "connect", StringComparison.Ordinal) && string.IsNullOrEmpty(targetSessionId))
        {
            targetSessionId = $"session-{Guid.NewGuid():N}";
        }

        var payload = parameters == null ? null : (JsonElement?)JsonSerializer.SerializeToElement(parameters);

        // 如果是 connect，重新包装载荷
        if (string.Equals(actionName, "connect", StringComparison.Ordinal) && payload != null)
        {
             var json = payload.Value.GetRawText();
             using var doc = JsonDocument.Parse(json);
             var root = doc.RootElement;
             
             string capabilityId = "";
             JsonElement uiParams;

             if (root.TryGetProperty("CapabilityId", out var capIdProp))
             {
                 capabilityId = capIdProp.GetString() ?? "";
                 uiParams = root.GetProperty("Parameters").Clone();
             }
             else 
             {
                 capabilityId = pluginId; 
                 uiParams = root.Clone();
             }
             
             var finalPayload = new 
             {
                 CapabilityId = capabilityId,
                 SessionId = targetSessionId,
                 Parameters = uiParams
             };
             payload = JsonSerializer.SerializeToElement(finalPayload);
        }

        var request = new PluginHostRequest(
            Guid.NewGuid().ToString("N"),
            actionName,
            targetSessionId,
            null,
            payload
        );

        var response = await runtime.Client.SendAsync(request, TimeSpan.FromSeconds(10));
        if (response != null && !response.Ok)
        {
            throw new Exception($"Plugin action failed: {response.Error}");
        }

        // 处理 Disconnect
        if (string.Equals(actionName, "disconnect", StringComparison.Ordinal))
        {
             if (!string.IsNullOrEmpty(targetSessionId))
             {
                 _eventBus.Publish(new SessionClosedEvent(targetSessionId));
             }
             return;
        }

        // 3. 特殊处理新 Session 的生成 (等待注册并发布事件)
        if (string.Equals(actionName, "connect", StringComparison.Ordinal))
        {
            // 通过获取到的 payload 确认 capabilityId
             var json = payload.Value.GetRawText();
             using var doc = JsonDocument.Parse(json);
             var capabilityId = doc.RootElement.GetProperty("CapabilityId").GetString()!;

            // 等待 Session 注册最长 5 秒
            // 注意：这里需要 runtime 支持 WaitForSessionRegisteredAsync
            // 如果不存在，我们可能需要在这里轮询或让 ProtocolService 负责通知
            
            // 查找能力信息以获取 UI 名称
            var cap = runtime.Capabilities?.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
            
            var session = new Session
            {
                Id = targetSessionId!,
                Name = cap?.Name ?? $"{pluginId}/{capabilityId}",
                Port = "plugin",
                BaudRate = 0,
                AdapterId = $"plugin:{pluginId}:{capabilityId}",
                PluginId = pluginId,
                CapabilityId = capabilityId,
                Status = SessionStatus.Connected,
                ParametersJson = payload.Value.GetProperty("Parameters").GetRawText()
            };

            _eventBus.Publish(new SessionCreatedEvent(session));
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
