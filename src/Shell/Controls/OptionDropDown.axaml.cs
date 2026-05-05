using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace ComCross.Shell.Controls;

public partial class OptionDropDown : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<OptionDropDown, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<OptionDropDown, object?>(
            nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> ToolTipTextProperty =
        AvaloniaProperty.Register<OptionDropDown, string>(nameof(ToolTipText), string.Empty);

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<OptionDropDown, bool>(
            nameof(IsOpen),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<OptionDropDown, string> SelectedLabelProperty =
        AvaloniaProperty.RegisterDirect<OptionDropDown, string>(
            nameof(SelectedLabel),
            o => o.SelectedLabel);

    public OptionDropDown()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedItemProperty)
        {
            RaisePropertyChanged(SelectedLabelProperty, string.Empty, SelectedLabel);
        }
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string SelectedLabel => GetItemLabel(SelectedItem);

    private void OnDropDownClick(object? sender, RoutedEventArgs e)
    {
        IsOpen = !IsOpen;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        IsOpen = false;
    }

    private static string GetItemLabel(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var property = item.GetType().GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }
}
