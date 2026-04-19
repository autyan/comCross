using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class WorkloadTabs : BaseUserControl
{
    public WorkloadTabs()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnNotificationsClick(object? sender, RoutedEventArgs e)
    {
        // Get MainWindow and toggle notifications
        var mainWindow = this.FindAncestorOfType<Window>();
        if (mainWindow?.DataContext is MainWindowViewModel vm)
        {
            vm.IsNotificationsOpen = !vm.IsNotificationsOpen;
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        // Get MainWindow and toggle settings
        var mainWindow = this.FindAncestorOfType<Window>();
        if (mainWindow?.DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = !vm.IsSettingsOpen;
        }
    }

    private void OnTabBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: WorkloadTabItemViewModel tabItem })
        {
            if (tabItem.ActivateCommand?.CanExecute(tabItem.Id) == true)
            {
                tabItem.ActivateCommand.Execute(tabItem.Id);
            }
        }
    }
}
