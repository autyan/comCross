using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.PluginSdk.UI;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;

namespace ComCross.Shell.Services;

public interface IConnectDialogService
{
    Task ShowAsync(Window owner, PluginManagerViewModel pluginManager);
}

public sealed class ConnectDialogService : IConnectDialogService
{
    private readonly IObjectFactory _objectFactory;
    private readonly PluginUiStateManager _pluginUiStateManager;

    public ConnectDialogService(IObjectFactory objectFactory, PluginUiStateManager pluginUiStateManager)
    {
        _objectFactory = objectFactory;
        _pluginUiStateManager = pluginUiStateManager;
    }

    public async Task ShowAsync(Window owner, PluginManagerViewModel pluginManager)
    {
        var dialog = _objectFactory.Create<ConnectDialog>();
        var viewInstanceId = Guid.NewGuid().ToString("N");
        var selectorVm = _objectFactory.Create<BusAdapterSelectorViewModel>(
            pluginManager,
            BusAdapterSelectorViewModel.BusAdapterViewKind,
            viewInstanceId);
        dialog.DataContext = _objectFactory.Create<ConnectDialogViewModel>(pluginManager, selectorVm);

        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _pluginUiStateManager.ClearViewScope(
                new PluginUiViewScope(BusAdapterSelectorViewModel.BusAdapterViewKind, viewInstanceId));
        }
    }
}
