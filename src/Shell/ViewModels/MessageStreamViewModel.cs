using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class MessageStreamViewModel : BaseViewModel
{
    private readonly IMessageStreamService _messageStream;
    private readonly SettingsService _settingsService;
    private readonly IItemVmFactory<LogMessageListItemViewModel, LogMessageListItemContext> _itemFactory;

    private IDisposable? _messageSubscription;
    private readonly PropertyChangedEventHandler _activeSessionPropertyChangedHandler;

    private Session? _activeSession;
    private string _searchQuery = string.Empty;
    private bool _isHexDisplayMode;
    private bool _isMetricsBarVisible;

    public MessageStreamViewModel(
        ILocalizationService localization,
        IMessageStreamService messageStream,
        SettingsService settingsService,
        DisplaySettingsViewModel display,
        IItemVmFactory<LogMessageListItemViewModel, LogMessageListItemContext> itemFactory)
        : base(localization)
    {
        _messageStream = messageStream;
        _settingsService = settingsService;
        Display = display;
        _itemFactory = itemFactory;
        _activeSessionPropertyChangedHandler = OnActiveSessionPropertyChanged;

        MessageItems = new ItemVmCollection<LogMessageListItemViewModel, LogMessageListItemContext>(_itemFactory);

        Display.PropertyChanged += OnDisplayPropertyChanged;
    }

    public DisplaySettingsViewModel Display { get; }

    public ItemVmCollection<LogMessageListItemViewModel, LogMessageListItemContext> MessageItems { get; }

    public Session? ActiveSession
    {
        get => _activeSession;
        private set => SetProperty(ref _activeSession, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            ApplyFilter();
        }
    }

    public bool IsHexDisplayMode
    {
        get => _isHexDisplayMode;
        set
        {
            if (SetProperty(ref _isHexDisplayMode, value))
            {
                OnPropertyChanged(nameof(DisplayModeLabel));
                RefreshDisplayMode();
            }
        }
    }

    public bool IsMetricsBarVisible
    {
        get => _isMetricsBarVisible;
        set => SetProperty(ref _isMetricsBarVisible, value);
    }

    public string DisplayModeLabel => IsHexDisplayMode ? "HEX" : "STR";

    public bool HasActiveSession => _activeSession is not null;

    public string CurrentSessionLabel => L["stream.session.current"];

    public string ActiveSessionDisplayName => _activeSession?.Name ?? L["stream.session.none"];

    public string ActiveSessionStatusLabel => _activeSession is null
        ? L["stream.session.none"]
        : _activeSession.Status == SessionStatus.Connected
            ? L["status.connected"]
            : L["status.disconnected"];

    public string ActiveSessionDetailText
    {
        get
        {
            if (_activeSession is null)
            {
                return L["stream.session.noneHint"];
            }

            if (!string.IsNullOrWhiteSpace(_activeSession.Endpoint))
            {
                return _activeSession.Endpoint;
            }

            return L["stream.session.endpointPlaceholder"];
        }
    }

    public string ActiveSessionTypeLabel
    {
        get
        {
            if (_activeSession is null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(_activeSession.DisplayTitle)
                ? _activeSession.DisplayTitle
                : L["stream.session.generic"];
        }
    }

    public void ToggleDisplayMode() => IsHexDisplayMode = !IsHexDisplayMode;

    public void ToggleMetricsBar() => IsMetricsBarVisible = !IsMetricsBarVisible;

    public void SetActiveSession(Session? session)
    {
        if (ReferenceEquals(_activeSession, session))
        {
            return;
        }

        if (_activeSession is not null)
        {
            _activeSession.PropertyChanged -= _activeSessionPropertyChangedHandler;
        }

        ActiveSession = session;
        RaiseSessionContextChanged();

        _messageSubscription?.Dispose();
        _messageSubscription = null;

        LoadMessages();

        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        _activeSession.PropertyChanged += _activeSessionPropertyChangedHandler;

        var sessionId = _activeSession.Id;
        _messageSubscription = _messageStream.Subscribe(sessionId, message =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeSession?.Id != sessionId)
                {
                    return;
                }

                MessageItems.Add(new LogMessageListItemContext(message, Display.TimestampFormat, IsHexDisplayMode));
                TrimMessages();
            });
        });
    }

    public void ClearView()
    {
        MessageItems.Clear();
    }

    private void LoadMessages()
    {
        MessageItems.Clear();

        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        var max = _settingsService.Current.Display.MaxMessages;

        var messages = _messageStream.GetMessages(_activeSession.Id, 0, max);
        foreach (var message in messages)
        {
            MessageItems.Add(new LogMessageListItemContext(message, Display.TimestampFormat, IsHexDisplayMode));
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            var filtered = _messageStream.Search(_activeSession.Id, _searchQuery);
            MessageItems.Clear();
            foreach (var message in filtered)
            {
                MessageItems.Add(new LogMessageListItemContext(message, Display.TimestampFormat, IsHexDisplayMode));
            }

            return;
        }

        // If no query, reload baseline (up to max).
        MessageItems.Clear();
        var max = _settingsService.Current.Display.MaxMessages;
        var messages = _messageStream.GetMessages(_activeSession.Id, 0, max);
        foreach (var message in messages)
        {
            MessageItems.Add(new LogMessageListItemContext(message, Display.TimestampFormat, IsHexDisplayMode));
        }
    }

    private void OnActiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _activeSession))
        {
            return;
        }

        if (e.PropertyName is nameof(Session.Name)
            or nameof(Session.Status)
            or nameof(Session.Endpoint)
            or nameof(Session.ParametersJson)
            or nameof(Session.PluginId)
            or nameof(Session.CapabilityId)
            or nameof(Session.ParentSessionId)
            or null
            or "")
        {
            RaiseSessionContextChanged();
        }
    }

    private void RaiseSessionContextChanged()
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(CurrentSessionLabel));
        OnPropertyChanged(nameof(ActiveSessionDisplayName));
        OnPropertyChanged(nameof(ActiveSessionStatusLabel));
        OnPropertyChanged(nameof(ActiveSessionDetailText));
        OnPropertyChanged(nameof(ActiveSessionTypeLabel));
    }

    private void RefreshDisplayMode()
    {
        foreach (var item in MessageItems)
        {
            item.UpdateDisplayMode(IsHexDisplayMode);
        }
    }

    private void TrimMessages()
    {
        var max = _settingsService.Current.Display.MaxMessages;
        while (MessageItems.Count > max)
        {
            MessageItems.RemoveAt(0);
        }
    }

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(DisplaySettingsViewModel.TimestampFormat), StringComparison.Ordinal))
        {
            return;
        }

        foreach (var item in MessageItems)
        {
            item.UpdateTimestampFormat(Display.TimestampFormat);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Display.PropertyChanged -= OnDisplayPropertyChanged;
            if (_activeSession is not null)
            {
                _activeSession.PropertyChanged -= _activeSessionPropertyChangedHandler;
            }
            _messageSubscription?.Dispose();
            _messageSubscription = null;
            MessageItems.Dispose();
        }

        base.Dispose(disposing);
    }
}
