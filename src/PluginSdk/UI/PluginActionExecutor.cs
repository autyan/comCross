namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件动作执行器
/// 负责封装 UI 层的指令发送给插件后端（BusAdapter / Session）
/// </summary>
public class PluginActionExecutor
{
    private readonly PluginUiStateManager _stateManager;
    private readonly IPluginCommunicationLink _link;

    public PluginActionExecutor(PluginUiStateManager stateManager, IPluginCommunicationLink link)
    {
        _stateManager = stateManager;
        _link = link;
    }

    /// <summary>
    /// 执行连接动作
    /// 将从 StateManager 提取当前 UI 状态作为连接参数
    /// </summary>
    public async Task ExecuteConnectAsync(string pluginId, string capabilityId, string? sessionId = null)
    {
        var parameters = _stateManager.GetState(sessionId);
        
        // 包装成标准连接负载
        var payload = new 
        {
            CapabilityId = capabilityId,
            SessionId = sessionId, // 允许为 null，由 Link 或 Host 补全
            Parameters = parameters
        };

        // 核心协议：连接动作传递所有当前 UI 状态
        await _link.SendActionAsync(pluginId, sessionId, "connect", payload);
    }

    /// <summary>
    /// 执行断开连接动作
    /// </summary>
    public async Task ExecuteDisconnectAsync(string pluginId, string sessionId)
    {
        await _link.SendActionAsync(pluginId, sessionId, "disconnect", null);
    }

    /// <summary>
    /// 执行普通业务动作
    /// </summary>
    public async Task ExecuteActionAsync(string pluginId, string? sessionId, string actionName, object? extraParams = null)
    {
        // 可以合并 UI 状态和额外参数
        var finalParams = _stateManager.GetState(sessionId);
        // ... 合并逻辑 (略)
        
        await _link.SendActionAsync(pluginId, sessionId, actionName, extraParams);
    }
}

/// <summary>
/// 抽象通信链路接口，解耦具体进程通信实现
/// </summary>
public interface IPluginCommunicationLink
{
    Task SendActionAsync(string pluginId, string? sessionId, string actionName, object? parameters);
}
