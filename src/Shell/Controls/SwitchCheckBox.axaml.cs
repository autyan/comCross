using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ComCross.Shell.Controls;

public partial class SwitchCheckBox : UserControl
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<SwitchCheckBox, bool>(
            nameof(IsChecked),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SwitchCheckBox, string>(nameof(Label), string.Empty);

    public static readonly RoutedEvent<RoutedEventArgs> ToggleRequestedEvent =
        RoutedEvent.Register<SwitchCheckBox, RoutedEventArgs>(
            nameof(ToggleRequested),
            RoutingStrategies.Bubble);

    public SwitchCheckBox()
    {
        InitializeComponent();
        AddHandler(ToggleRequestedEvent, OnDefaultToggleRequested);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public event EventHandler<RoutedEventArgs> ToggleRequested
    {
        add => AddHandler(ToggleRequestedEvent, value);
        remove => RemoveHandler(ToggleRequestedEvent, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsEnabled || e.Handled)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(ToggleRequestedEvent));
        e.Handled = true;
    }

    private void OnDefaultToggleRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || !IsEnabled)
        {
            return;
        }

        IsChecked = !IsChecked;
        e.Handled = true;
    }
}
