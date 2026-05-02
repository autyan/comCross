namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 容器抽象接口
/// 用于将生成的控件添加到 Shell 侧的布局容器中
/// </summary>
public interface IPluginUiContainer
{
    /// <summary>
    /// 获取平台相关的 UI 根对象（如 Avalonia 的 Panel）
    /// </summary>
    object Root { get; }

    /// <summary>
    /// 添加控件到容器中
    /// </summary>
    void AddControl(string name, IPluginUiControl control);

    /// <summary>
    /// 清空容器
    /// </summary>
    void Clear();
}
