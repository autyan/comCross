using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

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
        var owner = ShellContext.GetOwner(this);
        if (owner is null)
        {
            return;
        }

        await ShellContext.TestConnectDialogs.ShowAsync(owner, pluginManager, plugin);
    }
}
