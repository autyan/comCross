using Avalonia.Controls;
using ComCross.PluginSdk.UI;

namespace ComCross.Shell.Plugins.UI;

/// <summary>
/// Avalonia 版 UI 容器实现
/// </summary>
public class AvaloniaPluginUiContainer : IPluginUiContainer
{
    private readonly StackPanel _panel;

    public AvaloniaPluginUiContainer()
    {
        _panel = new StackPanel
        {
            Spacing = 8
        };
    }

    /// <summary>
    /// 获取底层的 Avalonia 控件以便挂载到主界面
    /// </summary>
    public Control GetPanel() => _panel;

    public object Root => _panel;

    public void AddControl(string name, IPluginUiControl control)
    {
        if (control is AvaloniaPluginUiControl avaloniaControl)
        {
            _panel.Children.Add(avaloniaControl.AvaloniaControl);
        }
    }

    public void Clear()
    {
        _panel.Children.Clear();
    }
}
