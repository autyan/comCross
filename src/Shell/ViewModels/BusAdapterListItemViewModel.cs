using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
        OnPropertyChanged(string.Empty);
    }

    public string Icon => Adapter.Icon;

    public StreamGeometry? IconGeometry => TryResolveIconGeometry(Adapter.Icon);

    public string Name => Adapter.Name;

    public string? Description => Adapter.Description;

    public bool IsEnabled => Adapter.IsEnabled;

    public string ComingSoonLabel => L["sidebar.busAdapter.comingSoon"];

    private static StreamGeometry? TryResolveIconGeometry(string icon)
    {
        var resource = TryFindIconResource(icon);
        if (resource is not null)
        {
            return resource;
        }

        return icon switch
        {
            "NetworkIcon" => StreamGeometry.Parse("M12 4v4M6 10h12M7 20h4v-6H7zM13 20h4v-6h-4zM10 10v4M14 10v4"),
            "ServerIcon" => StreamGeometry.Parse("M5 5h14v5H5zM5 14h14v5H5zM8 7.5h.01M8 16.5h.01"),
            "CableIcon" => StreamGeometry.Parse("M8 4v8a2 2 0 0 0 2 2h4a2 2 0 0 1 2 2v4M6 2h4v4H6zM14 2h4v4h-4zM6 18h4v4H6zM14 18h4v4h-4z"),
            _ => StreamGeometry.Parse("M9 3h6v4h2a4 4 0 0 1 4 4v2h-4v-2a2 2 0 0 0-2-2H9a2 2 0 0 0-2 2v2H3v-2a4 4 0 0 1 4-4h2zM5 15h4v6H5zM15 15h4v6h-4zM10 17h4v2h-4z")
        };
    }

    private static StreamGeometry? TryFindIconResource(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon) || Application.Current is null)
        {
            return null;
        }

        return Application.Current.TryFindResource(icon, out var resource)
            ? resource as StreamGeometry
            : null;
    }
}
