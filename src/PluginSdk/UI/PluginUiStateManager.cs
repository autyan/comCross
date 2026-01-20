using System.Collections.Concurrent;
using System.Text.Json;

namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 状态管理器
/// 负责跨进程状态同步与本地 UI 控件的值管理
/// </summary>
public class PluginUiStateManager
{
    // SessionId -> (FieldKey -> Value)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> _states = new();
    
    // SessionId -> (FieldKey -> List of Controls)
    // 一个状态位可能对应多个 UI 表现（虽然目前通常是 1:1）
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<IPluginUiControl>>> _registrations = new();

    private const string DefaultStateKey = "__default__";

    public event EventHandler<PluginUiStateChangedEvent>? UiStateChanged;

    /// <summary>
    /// Merge a batch of values into the in-memory cache.
    /// Intended for host-side seeding from persisted config before rendering controls.
    /// </summary>
    public void MergeState(string? sessionId, IReadOnlyDictionary<string, object> values)
    {
        foreach (var kvp in values)
        {
            UpdateState(sessionId, kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// 注册控件到特定的会话状态位
    /// </summary>
    public void RegisterControl(string? sessionId, string key, IPluginUiControl control)
    {
        var sid = sessionId ?? DefaultStateKey;
        var sessionRegs = _registrations.GetOrAdd(sid, _ => new());
        var keyRegs = sessionRegs.GetOrAdd(key, _ => new());
        
        lock (keyRegs)
        {
            if (!keyRegs.Contains(control))
                keyRegs.Add(control);
        }
    }

    /// <summary>
    /// 更新状态位（通常由后端/另一进程推送过来）
    /// </summary>
    public void UpdateState(string? sessionId, string key, object value)
    {
        var sid = sessionId ?? DefaultStateKey;
        
        // 更新缓存 (avoid re-entrancy loops)
        var sessionState = _states.GetOrAdd(sid, _ => new());
        if (sessionState.TryGetValue(key, out var existing) && Equals(existing, value))
        {
            return;
        }

        sessionState[key] = value;

        // 通知 UI 控件
        if (_registrations.TryGetValue(sid, out var sessionRegs))
        {
            if (sessionRegs.TryGetValue(key, out var controls))
            {
                lock (controls)
                {
                    foreach (var control in controls)
                    {
                        control.UpdateFromState(value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Update state coming from a UI control (user input).
    /// Emits an event with full plugin/capability context so the host can persist values.
    /// </summary>
    public void UpdateStateFromControl(
        string pluginId,
        string capabilityId,
        string? viewId,
        string? sessionId,
        string key,
        object? value,
        IPluginUiControl? source = null)
    {
        var sid = sessionId ?? DefaultStateKey;

        // Update cache (avoid loops)
        var sessionState = _states.GetOrAdd(sid, _ => new());
        if (sessionState.TryGetValue(key, out var existing) && Equals(existing, value))
        {
            return;
        }

        if (value is null)
        {
            sessionState.TryRemove(key, out _);
        }
        else
        {
            sessionState[key] = value;
        }

        // Notify UI controls (except the source control to avoid redundant updates)
        if (_registrations.TryGetValue(sid, out var sessionRegs))
        {
            if (sessionRegs.TryGetValue(key, out var controls))
            {
                lock (controls)
                {
                    foreach (var control in controls)
                    {
                        if (ReferenceEquals(control, source))
                        {
                            continue;
                        }

                        control.UpdateFromState(value);
                    }
                }
            }
        }

        UiStateChanged?.Invoke(this, new PluginUiStateChangedEvent(
            pluginId,
            capabilityId,
            sessionId,
            viewId,
            key,
            value));
    }

    /// <summary>
    /// 批量更新状态
    /// </summary>
    public void UpdateStates(string? sessionId, IDictionary<string, object> updates)
    {
        foreach (var kvp in updates)
        {
            UpdateState(sessionId, kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// 从 JSON 字符串更新会话状态
    /// </summary>
    public void UpdateSessionState(string sessionId, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (dict != null)
            {
                UpdateStates(sessionId, dict);
            }
        }
        catch { /* Ignore malformed JSON */ }
    }

    /// <summary>
    /// 更新单个控件状态（增量更新）
    /// </summary>
    public void UpdateControlState(string? sessionId, string json)
    {
         UpdateSessionState(sessionId ?? DefaultStateKey, json);
    }

    /// <summary>
    /// 切换上下文 (触发当前所有相关控件刷新)
    /// </summary>
    public void SwitchContext(string? sessionId)
    {
        var sid = sessionId ?? DefaultStateKey;
        if (!_registrations.TryGetValue(sid, out var sessionRegs)) return;
        
        var currentState = GetState(sid);
        foreach (var reg in sessionRegs)
        {
            if (currentState.TryGetValue(reg.Key, out var value))
            {
                lock (reg.Value)
                {
                    foreach (var control in reg.Value)
                    {
                        control.UpdateFromState(value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取当前快照
    /// </summary>
    public IDictionary<string, object> GetState(string? sessionId)
    {
        var sid = sessionId ?? DefaultStateKey;
        if (_states.TryGetValue(sid, out var sessionState))
        {
            return new Dictionary<string, object>(sessionState);
        }
        return new Dictionary<string, object>();
    }

    /// <summary>
    /// 清除会话状态
    /// </summary>
    public void ClearSession(string sessionId)
    {
        _states.TryRemove(sessionId, out _);
        _registrations.TryRemove(sessionId, out _);
    }
}
