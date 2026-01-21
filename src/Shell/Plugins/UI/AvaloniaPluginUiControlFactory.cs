using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Services;
using ComCross.Shell.Services;

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
            "select" => new AvaloniaComboBoxControl(field, _localization),
            "number" => new AvaloniaNumberControl(field),
            "checkbox" => new AvaloniaCheckBoxControl(field),
            "text" or _ => new AvaloniaTextBoxControl(field, _localization)
        };

        if (wrapLabel && !string.IsNullOrWhiteSpace(label) && inner is not AvaloniaCheckBoxControl)
        {
            return new AvaloniaLabeledControl(field.Key, label, inner, _localization, field.LabelKey);
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
        var labelText = !string.IsNullOrEmpty(action.LabelKey) ? _localization.Strings[action.LabelKey] : action.Label;
        var iconOnly = !string.IsNullOrWhiteSpace(action.Icon);

        var button = new Button
        {
            Content = iconOnly ? action.Icon : labelText,
            HorizontalAlignment = iconOnly ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch,
            Margin = iconOnly ? new Avalonia.Thickness(0) : new Avalonia.Thickness(0, 4),
            Width = iconOnly ? 32 : double.NaN,
            Height = iconOnly ? 32 : double.NaN,
            Padding = iconOnly ? new Avalonia.Thickness(0) : new Avalonia.Thickness(12, 6),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        if (iconOnly)
        {
            button.FontSize = 16;
        }

        if (iconOnly)
        {
            ToolTip.SetTip(button, labelText);
        }

        EventHandler<string>? languageChanged = null;
        if (!string.IsNullOrWhiteSpace(action.LabelKey))
        {
            languageChanged = (_, _) =>
            {
                var text = _localization.Strings[action.LabelKey];
                if (iconOnly)
                {
                    ToolTip.SetTip(button, text);
                }
                else
                {
                    button.Content = text;
                }
            };
            _localization.LanguageChanged += languageChanged;
            button.DetachedFromVisualTree += (_, _) =>
            {
                if (languageChanged is not null)
                {
                    _localization.LanguageChanged -= languageChanged;
                }
            };
        }
        
        button.Click += async (_, _) => 
        {
            try
            {
                button.IsEnabled = false;
                await executeAction();
            }
            catch (Exception ex)
            {
                // Never let plugin action exceptions crash the UI thread.
                await MessageBoxService.ShowErrorAsync(_localization.GetString("connection.error.failed"), ex.Message);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };

        return new AvaloniaGenericControl(action.Id, button);
    }
}
