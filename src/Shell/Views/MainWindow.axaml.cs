using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // Show connect dialog
            var dialog = new ConnectDialog
            {
                DataContext = vm
            };

            await dialog.ShowDialog(this);
        }
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.DisconnectAsync();
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearMessages();
        }
    }

    private void OnExportClick(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement export
    }
}
