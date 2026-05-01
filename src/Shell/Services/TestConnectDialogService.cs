using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;

namespace ComCross.Shell.Services;

public interface ITestConnectDialogService
{
    Task ShowAsync(Control source, PluginManagerViewModel pluginManager, PluginItemViewModel plugin);
}

public sealed class TestConnectDialogService : ITestConnectDialogService
{
    private readonly IObjectFactory _objectFactory;

    public TestConnectDialogService(IObjectFactory objectFactory)
    {
        _objectFactory = objectFactory;
    }

    public async Task ShowAsync(Control source, PluginManagerViewModel pluginManager, PluginItemViewModel plugin)
    {
        var options = pluginManager.GetCapabilityOptions(plugin.Id);
        if (options.Count == 0)
        {
            await pluginManager.TestConnectAsync(plugin);
            return;
        }

        var dialog = _objectFactory.Create<TestConnectDialog>();
        dialog.DataContext = _objectFactory.Create<TestConnectDialogViewModel>(options);

        TestConnectDialogResult? result;
        if (TopLevel.GetTopLevel(source) is Window owner)
        {
            result = await dialog.ShowDialog<TestConnectDialogResult?>(owner);
        }
        else
        {
            dialog.Show();
            return;
        }

        if (result is null)
        {
            return;
        }

        await pluginManager.TestConnectAsync(plugin, result.CapabilityId, result.ParametersJson);
    }
}
