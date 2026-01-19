using System.Collections.Concurrent;

namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 渲染器
/// 负责管理 UI 容器的创建、缓存以及控件的生成
/// </summary>
public class PluginUiRenderer
{
    private readonly IPluginUiControlFactory _factory;
    private readonly PluginUiStateManager _stateManager;
    private readonly PluginActionExecutor _actionExecutor;
    private readonly ConcurrentDictionary<string, IPluginUiContainer> _containerCache = new();
    private const string DefaultStateKey = "__default__";

    public PluginUiRenderer(IPluginUiControlFactory factory, PluginUiStateManager stateManager, PluginActionExecutor actionExecutor)
    {
        _factory = factory;
        _stateManager = stateManager;
        _actionExecutor = actionExecutor;
    }

    /// <summary>
    /// 获取或渲染插件 UI
    /// </summary>
    /// <param name="pluginId">插件 ID</param>
    /// <param name="capabilityId">能力 ID</param>
    /// <param name="schema">UI 定义</param>
    /// <param name="sessionId">会话 ID，为 null 时表示默认状态（未连接）</param>
    /// <param name="viewId">视图 ID (用于区分同一个插件的不同 UI 实例，如 ConnectDialog, Sidebar 等)</param>
    /// <returns>UI 容器</returns>
    public IPluginUiContainer GetOrRender(string pluginId, string capabilityId, PluginUiSchema schema, string? sessionId = null, string? viewId = null)
    {
        var cacheKey = $"{pluginId}:{capabilityId}:{(sessionId ?? DefaultStateKey)}:{(viewId ?? "default")}";
        
        return _containerCache.GetOrAdd(cacheKey, _ => 
        {
            var container = CreateNewContainer();
            RenderToContainer(container, pluginId, capabilityId, schema, sessionId);
            return container;
        });
    }

    /// <summary>
    /// 清除特定会话的 UI 缓存
    /// </summary>
    public void ClearCache(string pluginId, string capabilityId, string? sessionId = null, string? viewId = null)
    {
        var cacheKey = $"{pluginId}:{capabilityId}:{(sessionId ?? DefaultStateKey)}:{(viewId ?? "default")}";
        _containerCache.TryRemove(cacheKey, out _);
    }

    private void RenderToContainer(IPluginUiContainer container, string pluginId, string capabilityId, PluginUiSchema schema, string? sessionId)
    {
        container.Clear();
        
        // 渲染字段
        foreach (var field in schema.Fields)
        {
            var control = _factory.CreateControl(field);
            
            // 绑定状态更新逻辑 (Value)
            _stateManager.RegisterControl(sessionId, field.Key, control);
            
            // 绑定选项更新逻辑 (Options)
            if (!string.IsNullOrEmpty(field.OptionsStatePath))
            {
                _stateManager.RegisterControl(sessionId, field.OptionsStatePath, control);
            }
            
            // 初始化控件值 (从 StateManager 获取当前缓存值)
            var currentState = _stateManager.GetState(sessionId);
            if (currentState.TryGetValue(field.Key, out var value))
            {
                control.UpdateFromState(value);
            }

            container.AddControl(field.Key, control);
        }

        // 渲染动作按钮
        if (schema.Actions != null && schema.Actions.Count > 0)
        {
            foreach (var action in schema.Actions)
            {
                var actionControl = _factory.CreateActionControl(action, async () => 
                {
                    if (string.Equals(action.Id, "connect", StringComparison.Ordinal))
                    {
                        await _actionExecutor.ExecuteConnectAsync(pluginId, capabilityId, sessionId);
                    }
                    else if (string.Equals(action.Id, "disconnect", StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            await _actionExecutor.ExecuteDisconnectAsync(pluginId, sessionId);
                        }
                    }
                    else
                    {
                         await _actionExecutor.ExecuteActionAsync(pluginId, sessionId, action.Id);
                    }
                });

                if (actionControl != null)
                {
                    container.AddControl(action.Id, actionControl);
                }
            }
        }
    }

    /// <summary>
    /// 这里的实现通常需要由 Shell 层注入或者通过反射/DI 获取具体的容器实现
    /// 为了 SDK 的自洽性，我们需要一个注入机制或让 Shell 负责提供容器基类
    /// </summary>
    protected virtual IPluginUiContainer CreateNewContainer()
    {
        // 实际运行时由 Shell 的具体子类覆盖或通过工厂创建
        throw new System.NotImplementedException("PluginUiRenderer.CreateNewContainer must be implemented by Shell or provided via factory.");
    }
}
