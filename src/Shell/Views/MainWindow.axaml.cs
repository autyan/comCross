using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
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

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleSettings();
        }
    }

    private void OnNotificationsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleNotifications();
        }
    }

    private void OnSettingsCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = false;
        }
    }

    private void OnNotificationsCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsNotificationsOpen = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = false;
            vm.IsNotificationsOpen = false;
        }
    }
}
