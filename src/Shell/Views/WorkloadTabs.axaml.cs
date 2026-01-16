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
        AddHandler(PointerPressedEvent, OnTabBorderClicked, RoutingStrategies.Tunnel);
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

    private void OnTabBorderClicked(object? sender, PointerPressedEventArgs e)
    {
        // Find the Border with Name="TabBorder"
        if (e.Source is Control control)
        {
            var border = control.FindAncestorOfType<Border>();
            if (border?.Name == "TabBorder" && border.DataContext is WorkloadTabItemViewModel tabItem)
            {
                // Execute activate command
                if (tabItem.ActivateCommand?.CanExecute(tabItem.Id) == true)
                {
                    tabItem.ActivateCommand.Execute(tabItem.Id);
                }
                
                // Update visual state
                UpdateTabVisualState(border, true);
                
                // Update sibling tabs
                if (border.Parent is Panel panel)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is Border otherBorder && otherBorder != border && otherBorder.Name == "TabBorder")
                        {
                            UpdateTabVisualState(otherBorder, false);
                        }
                    }
                }
            }
        }
    }

    private void UpdateTabVisualState(Border border, bool isActive)
    {
        if (isActive)
        {
            border.Classes.Add("active");
            // Update TextBlock font weight
            if (border.Child is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBlock { Name: "TabName" } textBlock)
                    {
                        textBlock.FontWeight = Avalonia.Media.FontWeight.SemiBold;
                    }
                }
            }
        }
        else
        {
            border.Classes.Remove("active");
            // Update TextBlock font weight
            if (border.Child is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBlock { Name: "TabName" } textBlock)
                    {
                        textBlock.FontWeight = Avalonia.Media.FontWeight.Regular;
                    }
                }
            }
        }
    }
}
