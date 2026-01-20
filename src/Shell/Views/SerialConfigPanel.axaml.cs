using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;
using ComCross.PluginSdk.UI;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Views;

public partial class SerialConfigPanel : UserControl
{
    public SerialConfigPanel()
    {
        InitializeComponent();
    }

    private async void OnRefreshPortsClick(object? sender, RoutedEventArgs e)
    {
        // Legacy panel: device enumeration is now driven by plugin UI state.
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var dialog = App.ServiceProvider.GetRequiredService<ConnectDialog>();
            dialog.DataContext = vm;

            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        }
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var session = vm.ActiveSession;
            if (session?.PluginId is not { Length: > 0 })
            {
                return;
            }

            var executor = App.ServiceProvider.GetRequiredService<PluginActionExecutor>();
            await executor.ExecuteDisconnectAsync(session.PluginId, session.Id);
        }
    }
}
