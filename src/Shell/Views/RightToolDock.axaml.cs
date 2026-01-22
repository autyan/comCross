using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class RightToolDock : BaseUserControl
{
    public RightToolDock()
    {
        InitializeComponent();
    }

    private void OnSendTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.SelectedToolTab = ToolDockTab.Send;
        }
    }

    private void OnCommandsTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.SelectedToolTab = ToolDockTab.Commands;
        }
    }
    
    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.ClearMessages();
        }
    }
    
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            await vm.ExportAsync();
        }
    }
    
    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            var sendBox = this.FindControl<TextBox>("SendMessageBox");
            var addCr = this.FindControl<CheckBox>("AddCrCheckBox");
            var addLf = this.FindControl<CheckBox>("AddLfCheckBox");
            var clearAfterSend = this.FindControl<CheckBox>("ClearAfterSendCheckBox");
            
            if (sendBox != null && !string.IsNullOrWhiteSpace(sendBox.Text))
            {
                await vm.SendAsync(
                    sendBox.Text,
                    vm.IsSendHexMode,
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

    private void OnToggleSendMode(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.ToggleSendMode();
            e.Handled = true;
        }
    }
}
