using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Services;

namespace ComCross.Shell.Plugins.UI;

/// <summary>
/// Avalonia 版插件 UI 控件基类
/// </summary>
public abstract class AvaloniaPluginUiControl : IPluginUiControl
{
    public abstract Control AvaloniaControl { get; }
    
    public abstract string Name { get; }

    public abstract object? Value { get; set; }
    
    public bool IsEnabled 
    { 
        get => AvaloniaControl.IsEnabled; 
        set => AvaloniaControl.IsEnabled = value; 
    }

    public abstract event EventHandler<object?>? ValueChanged;

    public virtual void UpdateFromState(PluginUiControlUpdate update)
        => Value = update.Value;
    
    public abstract void Reset();
}

public class AvaloniaGenericControl : AvaloniaPluginUiControl
{
    private readonly string _name;
    private readonly Control _control;

    public AvaloniaGenericControl(string name, Control control)
    {
        _name = name;
        _control = control;
    }

    public override Control AvaloniaControl => _control;
    public override string Name => _name;
    public override object? Value { get; set; }
    public override event EventHandler<object?>? ValueChanged { add { } remove { } }
    public override void Reset() { }
}

public sealed class AvaloniaLabeledControl : AvaloniaPluginUiControl
{
    private readonly string _name;
    private readonly AvaloniaPluginUiControl _inner;
    private readonly StackPanel _panel;
    private readonly TextBlock _label;
    private readonly ILocalizationService? _localization;
    private readonly string? _labelKey;
    private EventHandler<string>? _languageChanged;

    public AvaloniaLabeledControl(string name, string label, AvaloniaPluginUiControl inner)
        : this(name, label, inner, localization: null, labelKey: null)
    {
    }

    public AvaloniaLabeledControl(string name, string label, AvaloniaPluginUiControl inner, ILocalizationService? localization, string? labelKey)
    {
        _name = name;
        _inner = inner;
        _localization = localization;
        _labelKey = labelKey;
        _panel = new StackPanel
        {
            Spacing = 4,
            Orientation = Orientation.Vertical
        };

        _label = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.LightGray
        };

        if (_localization is not null && !string.IsNullOrWhiteSpace(_labelKey))
        {
            _label.Text = _localization.Strings[_labelKey!];

            _languageChanged = (_, _) =>
            {
                _label.Text = _localization.Strings[_labelKey!];
            };

            _localization.LanguageChanged += _languageChanged;

            _panel.DetachedFromVisualTree += (_, _) =>
            {
                if (_localization is not null && _languageChanged is not null)
                {
                    _localization.LanguageChanged -= _languageChanged;
                }

                _languageChanged = null;
            };
        }

        _panel.Children.Add(_label);
        _panel.Children.Add(_inner.AvaloniaControl);

        _inner.ValueChanged += (_, v) => ValueChanged?.Invoke(this, v);
    }

    public override Control AvaloniaControl => _panel;
    public override string Name => _name;

    public override object? Value
    {
        get => _inner.Value;
        set => _inner.Value = value;
    }

    public override event EventHandler<object?>? ValueChanged;

    public override void UpdateFromState(PluginUiControlUpdate update) => _inner.UpdateFromState(update);
    public override void Reset() => _inner.Reset();
}

/// <summary>
/// 文本框适配器
/// </summary>
public class AvaloniaTextBoxControl : AvaloniaPluginUiControl
{
    private readonly TextBox _textBox;
    private readonly string _name;
    private readonly ILocalizationService _localization;
    private readonly string? _labelKey;
    private EventHandler<string>? _languageChanged;
    public override Control AvaloniaControl => _textBox;
    public override string Name => _name;

    public AvaloniaTextBoxControl(PluginUiField field, ILocalizationService localization)
    {
        _name = !string.IsNullOrWhiteSpace(field.Key) ? field.Key : field.Name;
        _localization = localization;
        _labelKey = string.IsNullOrWhiteSpace(field.LabelKey) ? null : field.LabelKey;

        _textBox = new TextBox
        {
            Watermark = field.Label,
            UseFloatingWatermark = true
        };

        if (!string.IsNullOrWhiteSpace(_labelKey))
        {
            _textBox.Watermark = _localization.Strings[_labelKey!];
            _languageChanged = (_, _) =>
            {
                _textBox.Watermark = _localization.Strings[_labelKey!];
            };
            _localization.LanguageChanged += _languageChanged;

            _textBox.DetachedFromVisualTree += (_, _) =>
            {
                if (_languageChanged is not null)
                {
                    _localization.LanguageChanged -= _languageChanged;
                }

                _languageChanged = null;
            };
        }

        _textBox.TextChanged += (s, e) => ValueChanged?.Invoke(this, _textBox.Text);
    }

    public override object? Value 
    { 
        get => _textBox.Text; 
        set => _textBox.Text = value?.ToString(); 
    }

    public override event EventHandler<object?>? ValueChanged;
    public override void Reset() => _textBox.Clear();
}

/// <summary>
/// 下拉框适配器
/// </summary>
public class AvaloniaComboBoxControl : AvaloniaPluginUiControl
{
    private readonly ComboBox _comboBox;
    private readonly string _name;
    private readonly ILocalizationService _localization;
    private bool _suppressSelectionChanged;

    private sealed class OptionItem
    {
        public OptionItem(object value, string display)
        {
            Value = value;
            Display = display;
        }

        public object Value { get; }
        public string Display { get; }

        public override string ToString() => Display;
    }

    public override Control AvaloniaControl => _comboBox;
    public override string Name => _name;

    public AvaloniaComboBoxControl(PluginUiField field, ILocalizationService localization)
    {
        _name = !string.IsNullOrWhiteSpace(field.Key) ? field.Key : field.Name;
        _localization = localization;
        _comboBox = new ComboBox();

        ApplyOptions(field.GetOptionsAsOptionList());

        _comboBox.SelectionChanged += (_, _) =>
        {
            if (_suppressSelectionChanged)
            {
                return;
            }

            ValueChanged?.Invoke(this, GetSelectedValue());
        };
    }

    public override object? Value 
    { 
        get => GetSelectedValue();
        set => SetSelectedValue(value);
    }

    public override event EventHandler<object?>? ValueChanged;

    public override void UpdateFromState(PluginUiControlUpdate update)
    {
        if (update.Kind == PluginUiControlUpdateKind.Options)
        {
            ApplyOptionsFromState(update.Value);
            return;
        }

        // Value update
        Value = update.Value;
    }

    public override void Reset() => _comboBox.SelectedIndex = -1;

    private void ApplyOptions(IReadOnlyList<PluginUiOption> options)
    {
        if (options.Count <= 0)
        {
            return;
        }

        var items = new List<OptionItem>(options.Count);
        foreach (var opt in options)
        {
            if (opt.Value is null)
            {
                continue;
            }

            var label = ResolveOptionLabel(opt);
            items.Add(new OptionItem(opt.Value, label));
        }

        ApplyItems(items);
    }

    private string ResolveOptionLabel(PluginUiOption opt)
    {
        if (!string.IsNullOrWhiteSpace(opt.LabelKey))
        {
            return _localization.Strings[opt.LabelKey];
        }

        if (!string.IsNullOrWhiteSpace(opt.Label))
        {
            return opt.Label;
        }

        return opt.Value?.ToString() ?? string.Empty;
    }

    private void ApplyItems(List<OptionItem> items)
    {
        var current = GetSelectedValue();
        _suppressSelectionChanged = true;
        try
        {
            _comboBox.ItemsSource = items;
            SetSelectedValue(current);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void ApplyOptionsFromState(object? value)
    {
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var items = new List<OptionItem>();
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                items.Add(new OptionItem(item, item.ToString() ?? string.Empty));
            }

            ApplyItems(items);
            return;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var items = new List<OptionItem>();
            foreach (var item in element.EnumerateArray())
            {
                if (TryParseOptionItem(item, out var parsed))
                {
                    items.Add(parsed);
                }
            }

            ApplyItems(items);
        }
    }

    private object? GetSelectedValue()
    {
        return _comboBox.SelectedItem is OptionItem item ? item.Value : _comboBox.SelectedItem;
    }

    private void SetSelectedValue(object? value)
    {
        if (_comboBox.ItemsSource is IEnumerable<OptionItem> items)
        {
            foreach (var item in items)
            {
                if (AreOptionValuesEqual(item.Value, value))
                {
                    _comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        _comboBox.SelectedItem = value;
    }

    private static bool AreOptionValuesEqual(object? optionValue, object? selectedValue)
    {
        if (optionValue is null || selectedValue is null)
        {
            return false;
        }

        // JsonElement comparison (common when state comes from JSON)
        if (selectedValue is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => AreOptionValuesEqual(optionValue, je.GetString()),
                JsonValueKind.Number => je.TryGetInt64(out var i64)
                    ? AreOptionValuesEqual(optionValue, i64)
                    : je.TryGetDouble(out var d)
                        ? AreOptionValuesEqual(optionValue, d)
                        : optionValue.ToString() == je.ToString(),
                JsonValueKind.True or JsonValueKind.False => AreOptionValuesEqual(optionValue, je.GetBoolean()),
                _ => optionValue.ToString() == je.ToString()
            };
        }

        if (TryGetDecimal(optionValue, out var a) && TryGetDecimal(selectedValue, out var b))
        {
            return a == b;
        }

        if (optionValue is string s1 && selectedValue is string s2)
        {
            return string.Equals(s1, s2, StringComparison.Ordinal);
        }

        return Equals(optionValue, selectedValue) || string.Equals(optionValue.ToString(), selectedValue.ToString(), StringComparison.Ordinal);
    }

    private static bool TryGetDecimal(object value, out decimal result)
    {
        result = 0;
        switch (value)
        {
            case byte b:
                result = b;
                return true;
            case short s:
                result = s;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case float f:
                result = (decimal)f;
                return true;
            case double d:
                result = (decimal)d;
                return true;
            case decimal m:
                result = m;
                return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Number:
                if (je.TryGetInt64(out var i64))
                {
                    result = i64;
                    return true;
                }

                if (je.TryGetDouble(out var jd))
                {
                    result = (decimal)jd;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private bool TryParseOptionItem(JsonElement element, out OptionItem item)
    {
        item = new OptionItem(string.Empty, string.Empty);

        if (element.ValueKind == JsonValueKind.String)
        {
            var s = element.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            item = new OptionItem(s, s);
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var i64))
            {
                item = new OptionItem(i64, i64.ToString());
                return true;
            }

            if (element.TryGetDouble(out var d))
            {
                item = new OptionItem(d, d.ToString());
                return true;
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            // Option object: { value, labelKey?, label? }
            if (!element.TryGetProperty("value", out var valueNode))
            {
                return false;
            }

            object? value = valueNode.ValueKind switch
            {
                JsonValueKind.String => valueNode.GetString(),
                JsonValueKind.Number => valueNode.TryGetInt64(out var i64) ? i64 : valueNode.TryGetDouble(out var d) ? d : valueNode.ToString(),
                JsonValueKind.True or JsonValueKind.False => valueNode.GetBoolean(),
                _ => valueNode.ToString()
            };

            if (value is null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return false;
            }

            string? labelKey = null;
            if (element.TryGetProperty("labelKey", out var labelKeyNode) && labelKeyNode.ValueKind == JsonValueKind.String)
            {
                labelKey = labelKeyNode.GetString();
            }

            string? label = null;
            if (element.TryGetProperty("label", out var labelNode) && labelNode.ValueKind == JsonValueKind.String)
            {
                label = labelNode.GetString();
            }

            var display = !string.IsNullOrWhiteSpace(labelKey)
                ? _localization.Strings[labelKey]
                : !string.IsNullOrWhiteSpace(label)
                    ? label
                    : value.ToString() ?? string.Empty;

            item = new OptionItem(value, display);
            return true;
        }

        return false;
    }
}

public sealed class AvaloniaNumberControl : AvaloniaPluginUiControl
{
    private readonly NumericUpDown _numeric;
    private readonly string _name;

    public override Control AvaloniaControl => _numeric;
    public override string Name => _name;

    public AvaloniaNumberControl(PluginUiField field)
    {
        _name = !string.IsNullOrWhiteSpace(field.Key) ? field.Key : field.Name;
        _numeric = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 10000000,
            Increment = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _numeric.ValueChanged += (_, _) => ValueChanged?.Invoke(this, _numeric.Value);
    }

    public override object? Value
    {
        get => _numeric.Value;
        set
        {
            if (value is null)
            {
                _numeric.Value = null;
                return;
            }

            if (value is int i)
            {
                _numeric.Value = i;
                return;
            }

            if (value is long l)
            {
                _numeric.Value = l;
                return;
            }

            if (value is double d)
            {
                _numeric.Value = (decimal)d;
                return;
            }

            if (value is decimal m)
            {
                _numeric.Value = m;
                return;
            }

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var jd))
                {
                    _numeric.Value = (decimal)jd;
                    return;
                }
            }

            if (double.TryParse(value.ToString(), out var parsed))
            {
                _numeric.Value = (decimal)parsed;
            }
        }
    }

    public override event EventHandler<object?>? ValueChanged;

    public override void Reset() => _numeric.Value = null;
}

public sealed class AvaloniaCheckBoxControl : AvaloniaPluginUiControl
{
    private readonly CheckBox _checkBox;
    private readonly string _name;

    public override Control AvaloniaControl => _checkBox;
    public override string Name => _name;

    public AvaloniaCheckBoxControl(PluginUiField field)
    {
        _name = !string.IsNullOrWhiteSpace(field.Key) ? field.Key : field.Name;
        _checkBox = new CheckBox
        {
            Content = field.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _checkBox.IsCheckedChanged += (_, _) => ValueChanged?.Invoke(this, _checkBox.IsChecked ?? false);
    }

    public override object? Value
    {
        get => _checkBox.IsChecked ?? false;
        set
        {
            if (value is bool b)
            {
                _checkBox.IsChecked = b;
                return;
            }

            if (value is JsonElement je && (je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False))
            {
                _checkBox.IsChecked = je.GetBoolean();
                return;
            }

            if (bool.TryParse(value?.ToString(), out var parsed))
            {
                _checkBox.IsChecked = parsed;
            }
        }
    }

    public override event EventHandler<object?>? ValueChanged;

    public override void Reset() => _checkBox.IsChecked = false;
}
