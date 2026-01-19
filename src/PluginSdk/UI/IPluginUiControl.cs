using System;
using System.Text.Json;

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
    /// 根据插件下发的 UI State 更新控件内容（如下拉选项、默认值、状态等）
    /// </summary>
    void UpdateFromState(object? state);

    /// <summary>
    /// 重置为初始默认状态
    /// </summary>
    void Reset();
}
