using System.Collections.Concurrent;
using System.Text.Json;
using System;

namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 状态管理器
/// 负责跨进程状态同步与本地 UI 控件的值管理
/// </summary>
public class PluginUiStateManager
{
    // ViewKind -> SessionId -> (FieldKey -> Value)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, object>>> _states = new();

    // ScopeKey -> SessionId -> (FieldKey -> List of Controls)
    // Use weak refs to avoid keeping detached controls alive indefinitely.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, List<RegisteredControlRef>>>> _registrations = new();

    // ViewKind -> set of active ScopeKeys
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _scopesByKind = new();

    private const string DefaultStateKey = "__default__";
    private const string ResourceSeparator = "::resource::";

    public event EventHandler<PluginUiStateChangedEvent>? UiStateChanged;

    private sealed class RegisteredControlRef
    {
        public RegisteredControlRef(IPluginUiControl control, PluginUiControlUpdateKind kind)
        {
            Ref = new WeakReference<IPluginUiControl>(control);
            Kind = kind;
        }

        public WeakReference<IPluginUiControl> Ref { get; }
        public PluginUiControlUpdateKind Kind { get; }
    }

    private static void UpdateControlFromState(IPluginUiControl control, string key, PluginUiControlUpdateKind kind, object? value)
    {
        control.UpdateFromState(new PluginUiControlUpdate(key, kind, value));
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Undefined => null,
                JsonValueKind.Null => null,
                JsonValueKind.String => je.GetString() ?? string.Empty,
                JsonValueKind.Number => je.TryGetInt64(out var i64)
                    ? i64
                    : je.TryGetDouble(out var d)
                        ? d
                        : je.ToString(),
                JsonValueKind.True or JsonValueKind.False => je.GetBoolean(),
                // Keep complex JSON values as JsonElement so callers that expect objects/arrays can still work.
                _ => je
            };
        }

        return value;
    }

    private static string GetContextKey(string? sessionId, string? resourceKind = null, string? resourceId = null)
    {
        var baseKey = sessionId ?? DefaultStateKey;
        if (string.IsNullOrWhiteSpace(resourceKind) || string.IsNullOrWhiteSpace(resourceId))
        {
            return baseKey;
        }

        return $"{baseKey}{ResourceSeparator}{resourceKind}{ResourceSeparator}{resourceId}";
    }

    /// <summary>
    /// Merge a batch of values into the in-memory cache.
    /// Intended for host-side seeding from persisted config before rendering controls.
    /// </summary>
    public void MergeState(
        PluginUiViewScope viewScope,
        string? sessionId,
        IReadOnlyDictionary<string, object> values,
        string? resourceKind = null,
        string? resourceId = null)
    {
        foreach (var kvp in values)
        {
            UpdateState(viewScope, sessionId, kvp.Key, kvp.Value, resourceKind, resourceId);
        }
    }

    /// <summary>
    /// Replace the in-memory state snapshot for a given (ViewKind, SessionId).
    /// This is used by the host to apply an authoritative committed state (e.g. last successful connect parameters)
    /// and intentionally discard any previous draft values.
    /// </summary>
    public void SetStateSnapshot(
        PluginUiViewScope viewScope,
        string? sessionId,
        IReadOnlyDictionary<string, object> values,
        string? resourceKind = null,
        string? resourceId = null)
    {
        var viewKind = viewScope.ViewKind;
        var sid = GetContextKey(sessionId, resourceKind, resourceId);

        var kindState = _states.GetOrAdd(viewKind, _ => new());
        var sessionState = kindState.GetOrAdd(sid, _ => new());

        // Clear existing keys (do not notify per-key removals; we'll refresh by applying new values).
        sessionState.Clear();

        foreach (var kvp in values)
        {
            UpdateState(viewScope, sessionId, kvp.Key, kvp.Value, resourceKind, resourceId);
        }
    }

    /// <summary>
    /// 注册控件到特定的会话状态位
    /// </summary>
    public void RegisterControl(
        PluginUiViewScope viewScope,
        string? sessionId,
        string key,
        IPluginUiControl control,
        string? resourceKind = null,
        string? resourceId = null)
    {
        RegisterControl(viewScope, sessionId, key, control, PluginUiControlUpdateKind.Value, resourceKind, resourceId);
    }

    /// <summary>
    /// Register a control binding with explicit update kind.
    /// Value bindings represent a field value, Options bindings represent an options list (e.g. ComboBox ItemsSource).
    /// </summary>
    public void RegisterControl(
        PluginUiViewScope viewScope,
        string? sessionId,
        string key,
        IPluginUiControl control,
        PluginUiControlUpdateKind kind,
        string? resourceKind = null,
        string? resourceId = null)
    {
        var viewKind = viewScope.ViewKind;
        var scopeKey = viewScope.ScopeKey;
        var sid = GetContextKey(sessionId, resourceKind, resourceId);

        var scopeSet = _scopesByKind.GetOrAdd(viewKind, _ => new());
        scopeSet.TryAdd(scopeKey, 0);

        var scopeRegs = _registrations.GetOrAdd(scopeKey, _ => new());
        var sessionRegs = scopeRegs.GetOrAdd(sid, _ => new());
        var keyRegs = sessionRegs.GetOrAdd(key, _ => new());
        
        lock (keyRegs)
        {
            // prune dead refs
            for (var i = keyRegs.Count - 1; i >= 0; i--)
            {
                if (!keyRegs[i].Ref.TryGetTarget(out _))
                {
                    keyRegs.RemoveAt(i);
                }
            }

            foreach (var existing in keyRegs)
            {
                if (existing.Ref.TryGetTarget(out var target) && ReferenceEquals(target, control))
                {
                    return;
                }
            }

            keyRegs.Add(new RegisteredControlRef(control, kind));
        }
    }

    /// <summary>
    /// 更新状态位（通常由后端/另一进程推送过来）
    /// </summary>
    public void UpdateState(
        PluginUiViewScope viewScope,
        string? sessionId,
        string key,
        object? value,
        string? resourceKind = null,
        string? resourceId = null)
    {
        value = NormalizeValue(value);
        var viewKind = viewScope.ViewKind;
        var sid = GetContextKey(sessionId, resourceKind, resourceId);
        
        // 更新缓存 (avoid re-entrancy loops)
        var kindState = _states.GetOrAdd(viewKind, _ => new());
        var sessionState = kindState.GetOrAdd(sid, _ => new());
        if (value is null)
        {
            if (!sessionState.TryRemove(key, out _))
            {
                return;
            }
        }
        else
        {
            if (sessionState.TryGetValue(key, out var existing) && Equals(existing, value))
            {
                return;
            }

            sessionState[key] = value;
        }

        // 通知 UI 控件：同一 ViewKind 的所有实例都要更新
        if (_scopesByKind.TryGetValue(viewKind, out var scopes))
        {
            foreach (var scopeKey in scopes.Keys)
            {
                if (!_registrations.TryGetValue(scopeKey, out var scopeRegs)
                    || !scopeRegs.TryGetValue(sid, out var sessionRegs))
                {
                    continue;
                }

                if (!sessionRegs.TryGetValue(key, out var controls))
                {
                    continue;
                }

                lock (controls)
                {
                    for (var i = controls.Count - 1; i >= 0; i--)
                    {
                        if (!controls[i].Ref.TryGetTarget(out var control))
                        {
                            controls.RemoveAt(i);
                            continue;
                        }

                        UpdateControlFromState(control, key, controls[i].Kind, value);
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
        PluginUiViewScope viewScope,
        string? sessionId,
        string? resourceKind,
        string? resourceId,
        string key,
        object? value,
        IPluginUiControl? source = null)
    {
        value = NormalizeValue(value);
        var viewKind = viewScope.ViewKind;
        var sid = GetContextKey(sessionId, resourceKind, resourceId);

        // Update cache (avoid loops)
        var kindState = _states.GetOrAdd(viewKind, _ => new());
        var sessionState = kindState.GetOrAdd(sid, _ => new());
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

        // Notify UI controls across all instances of the same ViewKind (except the source)
        if (_scopesByKind.TryGetValue(viewKind, out var scopes))
        {
            foreach (var scopeKey in scopes.Keys)
            {
                if (!_registrations.TryGetValue(scopeKey, out var scopeRegs)
                    || !scopeRegs.TryGetValue(sid, out var sessionRegs))
                {
                    continue;
                }

                if (!sessionRegs.TryGetValue(key, out var controls))
                {
                    continue;
                }

                lock (controls)
                {
                    for (var i = controls.Count - 1; i >= 0; i--)
                    {
                        if (!controls[i].Ref.TryGetTarget(out var control))
                        {
                            controls.RemoveAt(i);
                            continue;
                        }

                        if (ReferenceEquals(control, source))
                        {
                            continue;
                        }

                        UpdateControlFromState(control, key, PluginUiControlUpdateKind.Value, value);
                    }
                }
            }
        }

        UiStateChanged?.Invoke(this, new PluginUiStateChangedEvent(
            pluginId,
            capabilityId,
            sessionId,
            viewScope.ViewKind,
            viewScope.ViewInstanceId,
            resourceKind,
            resourceId,
            key,
            value));
    }

    /// <summary>
    /// 批量更新状态
    /// </summary>
    public void UpdateStates(
        PluginUiViewScope viewScope,
        string? sessionId,
        IDictionary<string, object> updates,
        string? resourceKind = null,
        string? resourceId = null)
    {
        foreach (var kvp in updates)
        {
            UpdateState(viewScope, sessionId, kvp.Key, kvp.Value, resourceKind, resourceId);
        }
    }

    /// <summary>
    /// 从 JSON 字符串更新会话状态
    /// </summary>
    public void UpdateSessionState(PluginUiViewScope viewScope, string sessionId, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (dict != null)
            {
                UpdateStates(viewScope, sessionId, dict);
            }
        }
        catch { /* Ignore malformed JSON */ }
    }

    /// <summary>
    /// 更新单个控件状态（增量更新）
    /// </summary>
        public void UpdateControlState(PluginUiViewScope viewScope, string? sessionId, string json)
    {
            UpdateSessionState(viewScope, sessionId ?? DefaultStateKey, json);
    }

    /// <summary>
    /// 切换上下文 (触发当前所有相关控件刷新)
    /// </summary>
    public void SwitchContext(
        PluginUiViewScope viewScope,
        string? sessionId,
        string? resourceKind = null,
        string? resourceId = null)
    {
        var scopeKey = viewScope.ScopeKey;
        var sid = GetContextKey(sessionId, resourceKind, resourceId);
        if (!_registrations.TryGetValue(scopeKey, out var scopeRegs)) return;
        if (!scopeRegs.TryGetValue(sid, out var sessionRegs)) return;
        
        var currentState = GetState(viewScope, sessionId, resourceKind, resourceId);
        foreach (var reg in sessionRegs)
        {
            if (currentState.TryGetValue(reg.Key, out var value))
            {
                lock (reg.Value)
                {
                    for (var i = reg.Value.Count - 1; i >= 0; i--)
                    {
                        if (!reg.Value[i].Ref.TryGetTarget(out var control))
                        {
                            reg.Value.RemoveAt(i);
                            continue;
                        }

                        UpdateControlFromState(control, reg.Key, reg.Value[i].Kind, value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取当前快照
    /// </summary>
    public IDictionary<string, object> GetState(
        PluginUiViewScope viewScope,
        string? sessionId,
        string? resourceKind = null,
        string? resourceId = null)
    {
        var viewKind = viewScope.ViewKind;
        var sid = GetContextKey(sessionId, resourceKind, resourceId);
        if (_states.TryGetValue(viewKind, out var kindState)
            && kindState.TryGetValue(sid, out var sessionState))
        {
            return new Dictionary<string, object>(sessionState);
        }
        return new Dictionary<string, object>();
    }

    /// <summary>
    /// 清除会话状态
    /// </summary>
    public void ClearSession(
        PluginUiViewScope viewScope,
        string sessionId,
        string? resourceKind = null,
        string? resourceId = null)
    {
        var viewKind = viewScope.ViewKind;
        var scopeKey = viewScope.ScopeKey;
        var contextKey = GetContextKey(sessionId, resourceKind, resourceId);

        if (_states.TryGetValue(viewKind, out var kindState))
        {
            kindState.TryRemove(contextKey, out _);
        }
        if (_registrations.TryGetValue(scopeKey, out var scopeRegs))
        {
            scopeRegs.TryRemove(contextKey, out _);
        }
    }

    /// <summary>
    /// Remove a view instance's control registrations without clearing shared state.
    /// Useful for ephemeral UIs (e.g. ConnectDialog) that use unique ViewInstanceId.
    /// </summary>
    public void ClearViewScope(PluginUiViewScope viewScope)
    {
        var viewKind = viewScope.ViewKind;
        var scopeKey = viewScope.ScopeKey;

        _registrations.TryRemove(scopeKey, out _);

        if (_scopesByKind.TryGetValue(viewKind, out var scopes))
        {
            scopes.TryRemove(scopeKey, out _);
            if (scopes.IsEmpty)
            {
                _scopesByKind.TryRemove(viewKind, out _);
            }
        }
    }
}
