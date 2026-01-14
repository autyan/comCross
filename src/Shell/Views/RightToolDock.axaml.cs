using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class RightToolDock : UserControl
{
    public static readonly StyledProperty<bool> IsCommandsTabActiveProperty =
        AvaloniaProperty.Register<RightToolDock, bool>(nameof(IsCommandsTabActive));

    public RightToolDock()
    {
        InitializeComponent();
    }

    public bool IsCommandsTabActive
    {
        get => GetValue(IsCommandsTabActiveProperty);
        set => SetValue(IsCommandsTabActiveProperty, value);
    }

    private void OnSendTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedToolTab = ToolDockTab.Send;
        }
    }

    private void OnCommandsTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedToolTab = ToolDockTab.Commands;
        }
    }
}
