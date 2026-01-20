using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for ConnectDialog.
/// This dialog previously bound directly to MainWindowViewModel; we now keep a 1:1 mapping.
/// </summary>
public sealed class ConnectDialogViewModel : BaseViewModel
{
    public ConnectDialogViewModel(
        ILocalizationService localization,
        PluginManagerViewModel pluginManager,
        BusAdapterSelectorViewModel busAdapterSelectorViewModel)
        : base(localization)
    {
        PluginManager = pluginManager;
        BusAdapterSelectorViewModel = busAdapterSelectorViewModel;
    }

    public PluginManagerViewModel PluginManager { get; }

    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel { get; }
}
