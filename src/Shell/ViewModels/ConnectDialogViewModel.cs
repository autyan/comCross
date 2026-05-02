using System;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for ConnectDialog.
/// This dialog previously bound directly to MainWindowViewModel; we now keep a 1:1 mapping.
/// </summary>
public sealed class ConnectDialogViewModel : BaseViewModel
{
    private readonly EventHandler _pluginsReloadedHandler;

    public ConnectDialogViewModel(
        ILocalizationService localization,
        PluginManagerViewModel pluginManager,
        BusAdapterSelectorViewModel busAdapterSelectorViewModel)
        : base(localization)
    {
        PluginManager = pluginManager;
        BusAdapterSelectorViewModel = busAdapterSelectorViewModel;

        // Make ConnectDialog self-contained: populate the adapter list just like MainWindow does.
        BusAdapterSelectorViewModel.UpdatePluginAdapters(PluginManager.GetAllCapabilityOptions());

        _pluginsReloadedHandler = (_, _) =>
        {
            BusAdapterSelectorViewModel.UpdatePluginAdapters(PluginManager.GetAllCapabilityOptions());
        };
        PluginManager.PluginsReloaded += _pluginsReloadedHandler;
    }

    public PluginManagerViewModel PluginManager { get; }

    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PluginManager.PluginsReloaded -= _pluginsReloadedHandler;
        }

        base.Dispose(disposing);
    }
}
