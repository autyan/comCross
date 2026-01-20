using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.Services;
using ComCross.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Views;

public partial class PluginManagerView : BaseUserControl
{
    public PluginManagerView()
    {
        InitializeComponent();
    }

    private async void OnTogglePluginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagerViewModel pluginManager)
        {
            return;
        }

        if (sender is CheckBox { DataContext: PluginItemViewModel plugin })
        {
            await pluginManager.ToggleAsync(plugin);
        }
    }

    private async void OnTestConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagerViewModel pluginManager)
        {
            return;
        }

        if (sender is Button { DataContext: PluginItemViewModel plugin })
        {
            await ShowTestConnectDialogAsync(pluginManager, plugin);
        }
    }

    private async Task ShowTestConnectDialogAsync(PluginManagerViewModel pluginManager, PluginItemViewModel plugin)
    {
        var options = pluginManager.GetCapabilityOptions(plugin.Id);
        if (options.Count == 0)
        {
            await pluginManager.TestConnectAsync(plugin);
            return;
        }

        var dialog = App.ServiceProvider.GetRequiredService<TestConnectDialog>();
        var objectFactory = App.ServiceProvider.GetRequiredService<IObjectFactory>();
        dialog.DataContext = objectFactory.Create<TestConnectDialogViewModel>(options);

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            dialog.Show();
            return;
        }

        var result = await dialog.ShowDialog<TestConnectDialogResult?>(owner);
        if (result is null)
        {
            return;
        }

        await pluginManager.TestConnectAsync(plugin, result.CapabilityId, result.ParametersJson);
    }
}
