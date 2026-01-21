using System;

namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 控件抽象接口
/// 屏蔽具体的 UI 框架（如 Avalonia）
/// </summary>
public interface IPluginUiControl
{
    /// <summary>
    /// 控件名称（对应 JsonSchema 中的属性名）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前值
    /// </summary>
    object? Value { get; set; }

    /// <summary>
    /// 控件是否可用
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 值变更事件
    /// </summary>
    event EventHandler<object?>? ValueChanged;

    /// <summary>
    /// Update control from host-provided state with explicit context.
    /// This is the only supported update mechanism.
    /// </summary>
    void UpdateFromState(PluginUiControlUpdate update);

    /// <summary>
    /// 重置为初始默认状态
    /// </summary>
    void Reset();
}
