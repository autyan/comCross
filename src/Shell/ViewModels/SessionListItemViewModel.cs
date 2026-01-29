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

    public string DisplayName => OverrideName ?? Name;

    public bool IsListener => Session.Kind == SessionKind.Listener;
    public bool IsConnection => !IsListener;
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

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Session.Name):
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Endpoint));
                break;
            case nameof(Session.Kind):
                OnPropertyChanged(nameof(IsListener));
                OnPropertyChanged(nameof(IsConnection));
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
}
