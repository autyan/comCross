using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class NotificationCenterView : UserControl
{
    public NotificationCenterView()
    {
        InitializeComponent();
    }

    private async void OnMarkAllReadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationCenterViewModel vm)
        {
            await vm.MarkAllReadAsync();
        }
    }

    private async void OnClearAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationCenterViewModel vm)
        {
            await vm.ClearAllAsync();
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationCenterViewModel vm && sender is Button button && button.CommandParameter is string id)
        {
            await vm.DeleteAsync(id);
        }
    }
}
