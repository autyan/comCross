using ComCross.Shared.Services;
using ComCross.Shell.Models;

namespace ComCross.Shell.ViewModels;

public sealed class BusAdapterListItemViewModel : LocalizedItemViewModelBase<BusAdapterInfo>
{
    private BusAdapterInfo? _adapter;

    public BusAdapterListItemViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public BusAdapterInfo Adapter => _adapter ?? throw new System.InvalidOperationException("BusAdapterListItemViewModel not initialized.");

    protected override void OnInit(BusAdapterInfo adapter)
    {
        _adapter = adapter;
        OnPropertyChanged(null);
    }

    public string Icon => Adapter.Icon;

    public string Name => Adapter.Name;

    public string? Description => Adapter.Description;

    public bool IsEnabled => Adapter.IsEnabled;

    public string ComingSoonLabel => L["sidebar.busAdapter.comingSoon"];
}
