using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
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
    public override event EventHandler<object?>? ValueChanged;
    public override void UpdateFromState(object? value) { }
    public override void Reset() { }
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
        _name = field.Key;
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
        _name = field.Key;
        _comboBox = new ComboBox();
        if (field.Options != null)
        {
            _comboBox.ItemsSource = field.Options;
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
