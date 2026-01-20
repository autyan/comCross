using ComCross.Shared.Services;
using ComCross.Shell.Models;

namespace ComCross.Shell.ViewModels;

public sealed class BusAdapterListItemViewModel : BaseViewModel
{
    public BusAdapterListItemViewModel(ILocalizationService localization, BusAdapterInfo adapter)
        : base(localization)
    {
        Adapter = adapter;
    }

    public BusAdapterInfo Adapter { get; }

    public string Icon => Adapter.Icon;

    public string Name => Adapter.Name;

    public string? Description => Adapter.Description;

    public bool IsEnabled => Adapter.IsEnabled;

    public string ComingSoonLabel => L["sidebar.busAdapter.comingSoon"];
}
