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

    public IPluginUiControl CreateControl(PluginUiField field, bool wrapLabel = true)
    {
        // Prefer `control` (schema v0.4+); fall back to legacy `type`.
        var kind = !string.IsNullOrWhiteSpace(field.Control) ? field.Control : field.Type;
        kind = kind?.Trim().ToLowerInvariant() ?? "text";

        var label = ResolveLabel(field);

        if (!string.IsNullOrWhiteSpace(label))
        {
            field.Label = label;
        }

        AvaloniaPluginUiControl inner = kind switch
        {
            "select" => new AvaloniaComboBoxControl(field),
            "number" => new AvaloniaNumberControl(field),
            "checkbox" => new AvaloniaCheckBoxControl(field),
            "text" or _ => new AvaloniaTextBoxControl(field)
        };

        if (wrapLabel && !string.IsNullOrWhiteSpace(label) && inner is not AvaloniaCheckBoxControl)
        {
            return new AvaloniaLabeledControl(field.Key, label, inner);
        }

        // Checkbox renders its own label.
        return inner;
    }

    private string ResolveLabel(PluginUiField field)
    {
        if (!string.IsNullOrWhiteSpace(field.LabelKey))
        {
            return _localization.Strings[field.LabelKey];
        }

        if (!string.IsNullOrWhiteSpace(field.Label))
        {
            return field.Label;
        }

        // fallback: show the field name for debugging usability
        return field.Name;
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
