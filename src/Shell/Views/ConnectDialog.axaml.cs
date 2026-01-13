using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;

namespace ComCross.Shell.Views;

public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && PortComboBox.SelectedItem is Device device)
        {
            var baudRateText = (BaudRateComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "115200";
            var baudRate = int.Parse(baudRateText);
            var sessionName = string.IsNullOrWhiteSpace(SessionNameTextBox.Text) ? device.Port : SessionNameTextBox.Text;

            await vm.ConnectAsync(device.Port, baudRate, sessionName);
            Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
