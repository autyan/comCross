using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class NotificationCenterView : BaseUserControl
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

    private async void OnNotificationMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: NotificationItemViewModel vm } && vm.CanOpenTargetDirectory)
        {
            await vm.OpenTargetDirectoryAsync();
            e.Handled = true;
        }
    }
}
