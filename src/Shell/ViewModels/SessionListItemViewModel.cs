using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class SessionListItemViewModel : BaseViewModel, IInitializable<Session>
{
    private Session? _session;
    private bool _isInitialized;

    private int _indentLevel;
    private string? _overrideName;
    private bool _isCollapsed;
    private int _childSessionCount;

    public SessionListItemViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public void Init(Session session)
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("SessionListItemViewModel already initialized.");
        }

        _isInitialized = true;
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;

        // Ensure UI refresh on initial attach.
        OnPropertyChanged(string.Empty);
    }

    public Session Session => _session ?? throw new InvalidOperationException("SessionListItemViewModel not initialized.");

    public int IndentLevel
    {
        get => _indentLevel;
        set
        {
            if (SetProperty(ref _indentLevel, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(HasParent));
            }
        }
    }

    public string? OverrideName
    {
        get => _overrideName;
        set
        {
            if (SetProperty(ref _overrideName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Endpoint));
            }
        }
    }

    public string Name => Session.Name;

    public string DisplayName => OverrideName ?? ResolveDisplayName();
    public string? Subtitle => string.IsNullOrWhiteSpace(Session.DisplaySubtitle) ? null : Session.DisplaySubtitle;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public StreamGeometry IconGeometry => ResolveIconGeometry(Session.DisplayIcon);
    public bool IsGroupSession => ChildSessionCount > 0 || Session.ManagedResourceKinds.Count > 0;
    public bool IsLeafSession => !IsGroupSession;
    public bool ShowChevron => ChildSessionCount > 0;

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetProperty(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(ChevronGlyph));
            }
        }
    }

    public string ChevronGlyph => IsCollapsed ? "▶" : "▼";
    public bool ShowStats => IsLeafSession;
    public bool HasParent => IndentLevel > 0;

    public int ChildSessionCount
    {
        get => _childSessionCount;
        set
        {
            if (SetProperty(ref _childSessionCount, value))
            {
                OnPropertyChanged(nameof(ChildSummaryText));
                OnPropertyChanged(nameof(HasChildSessions));
                OnPropertyChanged(nameof(IsGroupSession));
                OnPropertyChanged(nameof(IsLeafSession));
                OnPropertyChanged(nameof(ShowChevron));
                OnPropertyChanged(nameof(ShowStats));
            }
        }
    }

    public bool HasChildSessions => ChildSessionCount > 0;

    public string ChildSummaryText => string.Format(L["network.session.listener.connections"], ChildSessionCount);

    public string Endpoint
    {
        get
        {
            var endpoint = Session.Endpoint;
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                return endpoint;
            }

            // For simplified listener connections, we override the display name to "Conn #n".
            // Preserve the original session name (often remote endpoint) as the subtitle.
            if (OverrideName is not null && !string.IsNullOrWhiteSpace(Session.Name))
            {
                return Session.Name;
            }

            return string.Empty;
        }
    }
    public long RxBytes => Session.RxBytes;
    public long TxBytes => Session.TxBytes;
    public SessionStatus Status => Session.Status;

    public string TxLabel => L["status.tx"];
    public string RxLabel => L["status.rx"];

    private string ResolveDisplayName()
    {
        if (string.IsNullOrWhiteSpace(Session.DisplayTitle))
        {
            return Name;
        }

        if (string.IsNullOrWhiteSpace(Name)
            || string.Equals(Name, Session.Endpoint, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(Session.CapabilityId)
                // i18n-ignore (non-UI generated session id prefix match)
                && Name.StartsWith($"{Session.CapabilityId} #", StringComparison.Ordinal)))
        {
            return Session.DisplayTitle!;
        }

        return Name;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Session.Name):
            case nameof(Session.DisplayTitle):
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Endpoint));
                break;
            case nameof(Session.DisplaySubtitle):
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(Endpoint));
                break;
            case nameof(Session.DisplayIcon):
                OnPropertyChanged(nameof(IconGeometry));
                break;
            case nameof(Session.ManagedResourceKinds):
            case nameof(Session.PluginId):
            case nameof(Session.CapabilityId):
                OnPropertyChanged(nameof(IsGroupSession));
                OnPropertyChanged(nameof(IsLeafSession));
                OnPropertyChanged(nameof(IconGeometry));
                OnPropertyChanged(nameof(ShowChevron));
                OnPropertyChanged(nameof(ShowStats));
                break;
            case nameof(Session.ParametersJson):
            case nameof(Session.Endpoint):
                OnPropertyChanged(nameof(Endpoint));
                break;
            case nameof(Session.RxBytes):
                OnPropertyChanged(nameof(RxBytes));
                break;
            case nameof(Session.TxBytes):
                OnPropertyChanged(nameof(TxBytes));
                break;
            case nameof(Session.Status):
                OnPropertyChanged(nameof(Status));
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session != null)
            {
                _session.PropertyChanged -= OnSessionPropertyChanged;
            }
        }

        base.Dispose(disposing);
    }

    private static StreamGeometry ResolveIconGeometry(string? icon)
    {
        var key = string.IsNullOrWhiteSpace(icon) ? "PluginIcon" : icon;
        if (Application.Current is not null && Application.Current.TryFindResource(key, out var resource) && resource is StreamGeometry geometry)
        {
            return geometry;
        }

        return key switch
        {
            "NetworkIcon" => StreamGeometry.Parse("M12 4v4M6 10h12M7 20h4v-6H7zM13 20h4v-6h-4zM10 10v4M14 10v4"),
            "ServerIcon" => StreamGeometry.Parse("M5 5h14v5H5zM5 14h14v5H5zM8 7.5h.01M8 16.5h.01"),
            "CableIcon" => StreamGeometry.Parse("M8 4v8a2 2 0 0 0 2 2h4a2 2 0 0 1 2 2v4M6 2h4v4H6zM14 2h4v4h-4zM6 18h4v4H6zM14 18h4v4h-4z"),
            _ => StreamGeometry.Parse("M9 3h6v4h2a4 4 0 0 1 4 4v2h-4v-2a2 2 0 0 0-2-2H9a2 2 0 0 0-2 2v2H3v-2a4 4 0 0 1 4-4h2zM5 15h4v6H5zM15 15h4v6h-4zM10 17h4v2h-4z")
        };
    }
}
