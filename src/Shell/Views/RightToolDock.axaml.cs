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
    
    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearMessages();
        }
    }
    
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ExportAsync();
        }
    }
    
    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var sendBox = this.FindControl<TextBox>("SendMessageBox");
            var hexMode = this.FindControl<CheckBox>("HexModeCheckBox");
            var addCr = this.FindControl<CheckBox>("AddCrCheckBox");
            var addLf = this.FindControl<CheckBox>("AddLfCheckBox");
            var clearAfterSend = this.FindControl<CheckBox>("ClearAfterSendCheckBox");
            
            if (sendBox != null && !string.IsNullOrWhiteSpace(sendBox.Text))
            {
                await vm.SendAsync(
                    sendBox.Text,
                    hexMode?.IsChecked ?? false,
                    addCr?.IsChecked ?? false,
                    addLf?.IsChecked ?? false);
                    
                // Clear only if option is checked
                if (clearAfterSend?.IsChecked ?? false)
                {
                    sendBox.Text = string.Empty;
                }
            }
        }
    }
}
