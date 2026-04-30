using System;
using System.ComponentModel;
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
    private int _listenerChildCount;

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
                OnPropertyChanged(nameof(ShowConnectionBadge));
                OnPropertyChanged(nameof(ConnectionBadgeText));
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

    public bool IsListener => Session.Kind == SessionKind.Listener;
    public bool IsConnection => !IsListener;
    public bool IsNetworkConnection
        => IsConnection && string.Equals(Session.PluginId, "network.adapter", StringComparison.Ordinal);
    public bool IsSerialConnection
        => IsConnection && string.Equals(Session.CapabilityId, "serial", StringComparison.Ordinal);
    public bool ShowChevron => IsListener;

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
    public bool ShowStats => !IsListener;
    public bool HasParent => IndentLevel > 0;
    public bool ShowConnectionBadge
        => IsConnection && string.Equals(Session.PluginId, "network.adapter", StringComparison.Ordinal);

    public string ConnectionBadgeText
        => HasParent ? L["network.session.badge.inbound"] : L["network.session.badge.client"];

    public int ListenerChildCount
    {
        get => _listenerChildCount;
        set
        {
            if (SetProperty(ref _listenerChildCount, value))
            {
                OnPropertyChanged(nameof(ListenerSummaryText));
                OnPropertyChanged(nameof(HasListenerChildren));
            }
        }
    }

    public bool HasListenerChildren => ListenerChildCount > 0;

    public string ListenerSummaryText => string.Format(L["network.session.listener.connections"], ListenerChildCount);

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
            case nameof(Session.Kind):
            case nameof(Session.PluginId):
            case nameof(Session.CapabilityId):
                OnPropertyChanged(nameof(IsListener));
                OnPropertyChanged(nameof(IsConnection));
                OnPropertyChanged(nameof(IsNetworkConnection));
                OnPropertyChanged(nameof(IsSerialConnection));
                OnPropertyChanged(nameof(ShowChevron));
                OnPropertyChanged(nameof(ShowStats));
                OnPropertyChanged(nameof(ShowConnectionBadge));
                OnPropertyChanged(nameof(ConnectionBadgeText));
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
}
