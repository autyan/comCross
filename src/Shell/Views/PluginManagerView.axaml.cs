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
        if (DataContext is not SettingsViewModel settings)
        {
            return;
        }

        if (sender is CheckBox { DataContext: PluginItemViewModel plugin })
        {
            await settings.PluginManager.ToggleAsync(plugin);
        }
    }
}
