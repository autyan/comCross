namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 控件工厂接口
/// 由 Shell 层实现，负责创建平台相关的具体控件
/// </summary>
public interface IPluginUiControlFactory
{
    /// <summary>
    /// 根据字段定义创建对应的控件
    /// </summary>
    IPluginUiControl CreateControl(PluginUiField field);

    /// <summary>
    /// 根据定义的动作创建一个 UI 控件 (通常是按钮)
    /// </summary>
    IPluginUiControl CreateActionControl(PluginUiAction action, Func<Task> executeAction);
}
