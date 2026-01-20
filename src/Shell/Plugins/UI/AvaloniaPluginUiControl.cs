using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using ComCross.PluginSdk.UI;

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

    public abstract void UpdateFromState(object? value);
    
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
    public override void UpdateFromState(object? value) { }
    public override void Reset() { }
}

public sealed class AvaloniaLabeledControl : AvaloniaPluginUiControl
{
    private readonly string _name;
    private readonly AvaloniaPluginUiControl _inner;
    private readonly StackPanel _panel;

    public AvaloniaLabeledControl(string name, string label, AvaloniaPluginUiControl inner)
    {
        _name = name;
        _inner = inner;
        _panel = new StackPanel
        {
            Spacing = 4,
            Orientation = Orientation.Vertical
        };

        _panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.LightGray
        });
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

    public override void UpdateFromState(object? value) => _inner.UpdateFromState(value);
    public override void Reset() => _inner.Reset();
}

/// <summary>
/// 文本框适配器
/// </summary>
public class AvaloniaTextBoxControl : AvaloniaPluginUiControl
{
    private readonly TextBox _textBox;
    private readonly string _name;
    public override Control AvaloniaControl => _textBox;
    public override string Name => _name;

    public AvaloniaTextBoxControl(PluginUiField field)
    {
        _name = !string.IsNullOrWhiteSpace(field.Key) ? field.Key : field.Name;
        _textBox = new TextBox
        {
            Watermark = field.Label,
            UseFloatingWatermark = true
        };
        _textBox.TextChanged += (s, e) => ValueChanged?.Invoke(this, _textBox.Text);
    }

    public override object? Value 
    { 
        get => _textBox.Text; 
        set => _textBox.Text = value?.ToString(); 
    }

    public override event EventHandler<object?>? ValueChanged;

    public override void UpdateFromState(object? value) => Value = value;
    public override void Reset() => _textBox.Clear();
}

/// <summary>
/// 下拉框适配器
/// </summary>
public class AvaloniaComboBoxControl : AvaloniaPluginUiControl
{
    private readonly ComboBox _comboBox;
    private readonly string _name;
    public override Control AvaloniaControl => _comboBox;
    public override string Name => _name;

    public AvaloniaComboBoxControl(PluginUiField field)
    {
        _name = !string.IsNullOrWhiteSpace(field.Key) ? field.Key : field.Name;
        _comboBox = new ComboBox();
        var initialOptions = field.GetOptionsAsOptionList();
        if (initialOptions.Count > 0)
        {
            var items = new List<string>(initialOptions.Count);
            foreach (var opt in initialOptions)
            {
                if (!string.IsNullOrWhiteSpace(opt.Value))
                {
                    items.Add(opt.Value);
                }
            }
            _comboBox.ItemsSource = items;
        }
        _comboBox.SelectionChanged += (s, e) => ValueChanged?.Invoke(this, _comboBox.SelectedItem);
    }

    public override object? Value 
    { 
        get => _comboBox.SelectedItem; 
        set => _comboBox.SelectedItem = value; 
    }

    public override event EventHandler<object?>? ValueChanged;

    public override void UpdateFromState(object? value)
    {
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var item in enumerable)
            {
                list.Add(item?.ToString() ?? "");
            }
            _comboBox.ItemsSource = list;
        }
        else if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(item.ToString());
            }
            _comboBox.ItemsSource = list;
        }
        else
        {
            Value = value;
        }
    }

    public override void Reset() => _comboBox.SelectedIndex = -1;
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

    public override void UpdateFromState(object? value) => Value = value;

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

    public override void UpdateFromState(object? value) => Value = value;

    public override void Reset() => _checkBox.IsChecked = false;
}
