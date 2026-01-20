using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
            RenderToContainer(container, pluginId, capabilityId, schema, sessionId, viewId);
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

    protected virtual void RenderToContainer(IPluginUiContainer container, string pluginId, string capabilityId, PluginUiSchema schema, string? sessionId, string? viewId)
    {
        container.Clear();

        var controls = BuildControls(pluginId, capabilityId, schema, sessionId, viewId);

        foreach (var field in schema.Fields)
        {
            if (controls.TryGetValue(field.Key, out var control))
            {
                container.AddControl(field.Key, control);
            }
        }

        if (schema.Actions != null && schema.Actions.Count > 0)
        {
            foreach (var action in schema.Actions)
            {
                if (controls.TryGetValue(action.Id, out var actionControl))
                {
                    container.AddControl(action.Id, actionControl);
                }
            }
        }
    }

    protected IDictionary<string, IPluginUiControl> BuildControls(string pluginId, string capabilityId, PluginUiSchema schema, string? sessionId, string? viewId)
    {
        var controls = new Dictionary<string, IPluginUiControl>(StringComparer.Ordinal);

        var wrapLabel = schema.Layout is null;

        // Fields
        foreach (var field in schema.Fields)
        {
            var control = _factory.CreateControl(field, wrapLabel);

            // Value binding
            _stateManager.RegisterControl(sessionId, field.Key, control);

            // Sync UI -> state (capture user edits)
            control.ValueChanged += (_, v) =>
            {
                _stateManager.UpdateStateFromControl(pluginId, capabilityId, viewId, sessionId, field.Key, v, control);
            };

            // Options binding
            if (!string.IsNullOrEmpty(field.OptionsStatePath))
            {
                _stateManager.RegisterControl(sessionId, field.OptionsStatePath, control);
            }

            // init from current state cache
            var currentState = _stateManager.GetState(sessionId);
            if (currentState.TryGetValue(field.Key, out var value))
            {
                control.UpdateFromState(value);
            }

            controls[field.Key] = control;
        }

        // Actions
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

                controls[action.Id] = actionControl;
            }
        }

        return controls;
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
