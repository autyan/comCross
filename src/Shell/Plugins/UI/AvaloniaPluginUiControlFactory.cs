using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Services;

namespace ComCross.Shell.Plugins.UI;

public class AvaloniaPluginUiControlFactory : IPluginUiControlFactory
{
    private readonly ILocalizationService _localization;

    public AvaloniaPluginUiControlFactory(ILocalizationService localization)
    {
        _localization = localization;
    }

    public IPluginUiControl CreateControl(PluginUiField field)
    {
        // 根据 field.Type 选择具体的适配器
        return field.Type switch
        {
            "select" => new AvaloniaComboBoxControl(field),
            "text" or _ => new AvaloniaTextBoxControl(field)
        };
    }

    public IPluginUiControl CreateActionControl(PluginUiAction action, Func<Task> executeAction)
    {
        var button = new Button
        {
            Content = !string.IsNullOrEmpty(action.LabelKey) ? _localization.Strings[action.LabelKey] : action.Label,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Margin = new Avalonia.Thickness(0, 4)
        };
        
        button.Click += async (_, _) => 
        {
            try
            {
                button.IsEnabled = false;
                await executeAction();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };

        return new AvaloniaGenericControl(action.Id, button);
    }
}
