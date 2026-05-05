using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class SettingsView : BaseUserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnRefreshStorageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.RefreshStorageSummary();
        }
    }

    private void OnOpenDataDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.OpenDataDirectory();
        }
    }

    private void OnOpenSpoolDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.OpenSessionSpoolDirectory();
        }
    }
}
