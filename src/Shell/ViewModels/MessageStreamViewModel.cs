using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed record MessageDisplayDensityOption(MessageDisplayDensity Density, string Label);

public sealed class MessageStreamViewModel : BaseViewModel
{
    private readonly IMessageStreamService _messageStream;
    private readonly IMessageFrameQueryService _messageFrameQuery;
    private readonly SettingsService _settingsService;
    private readonly IItemVmFactory<LogMessageListItemViewModel, LogMessageListItemContext> _itemFactory;

    private IDisposable? _messageSubscription;
    private readonly PropertyChangedEventHandler _activeSessionPropertyChangedHandler;

    private Session? _activeSession;
    private string _searchQuery = string.Empty;
    private PayloadRenderMode _payloadRenderMode = PayloadRenderMode.String;
    private MessageDisplayDensity _displayDensity = MessageDisplayDensity.Detailed;
    private MessageFrameDataSource _dataSource = MessageFrameDataSource.LiveSpool;
    private MessageDisplayDensityOption? _selectedDisplayDensityOption;
    private bool _isMetricsBarVisible;

    public MessageStreamViewModel(
        ILocalizationService localization,
        IMessageStreamService messageStream,
        IMessageFrameQueryService messageFrameQuery,
        SettingsService settingsService,
        DisplaySettingsViewModel display,
        IItemVmFactory<LogMessageListItemViewModel, LogMessageListItemContext> itemFactory)
        : base(localization)
    {
        _messageStream = messageStream;
        _messageFrameQuery = messageFrameQuery;
        _settingsService = settingsService;
        Display = display;
        _itemFactory = itemFactory;
        _activeSessionPropertyChangedHandler = OnActiveSessionPropertyChanged;
        _payloadRenderMode = _settingsService.Current.Export.DefaultPayloadRenderMode;
        DisplayDensityOptions =
        [
            new(MessageDisplayDensity.Plain, L["stream.density.plain"]),
            new(MessageDisplayDensity.Slim, L["stream.density.slim"]),
            new(MessageDisplayDensity.Detailed, L["stream.density.detailed"])
        ];
        _selectedDisplayDensityOption = DisplayDensityOptions.First(x => x.Density == _displayDensity);

        MessageItems = new ItemVmCollection<LogMessageListItemViewModel, LogMessageListItemContext>(_itemFactory);

        Display.PropertyChanged += OnDisplayPropertyChanged;
    }

    public DisplaySettingsViewModel Display { get; }

    public ItemVmCollection<LogMessageListItemViewModel, LogMessageListItemContext> MessageItems { get; }

    public IReadOnlyList<MessageDisplayDensityOption> DisplayDensityOptions { get; }

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

    public PayloadRenderMode PayloadRenderMode
    {
        get => _payloadRenderMode;
        set
        {
            if (SetProperty(ref _payloadRenderMode, value))
            {
                OnPropertyChanged(nameof(DisplayModeLabel));
                RefreshPayloadRenderMode();
            }
        }
    }

    public MessageDisplayDensity DisplayDensity
    {
        get => _displayDensity;
        set
        {
            if (SetProperty(ref _displayDensity, value))
            {
                OnPropertyChanged(nameof(DisplayDensityLabel));
                _selectedDisplayDensityOption = DisplayDensityOptions.FirstOrDefault(x => x.Density == value);
                OnPropertyChanged(nameof(SelectedDisplayDensityOption));
                RefreshDisplayDensity();
            }
        }
    }

    public MessageDisplayDensityOption? SelectedDisplayDensityOption
    {
        get => _selectedDisplayDensityOption;
        set
        {
            if (value is null || Equals(_selectedDisplayDensityOption, value))
            {
                return;
            }

            _selectedDisplayDensityOption = value;
            OnPropertyChanged();
            DisplayDensity = value.Density;
        }
    }

    public MessageFrameDataSource DataSource
    {
        get => _dataSource;
        private set
        {
            if (SetProperty(ref _dataSource, value))
            {
                OnPropertyChanged(nameof(DataSourceLabel));
                LoadMessages();
            }
        }
    }

    public bool IsMetricsBarVisible
    {
        get => _isMetricsBarVisible;
        set => SetProperty(ref _isMetricsBarVisible, value);
    }

    public string DisplayModeLabel => PayloadRenderMode == PayloadRenderMode.Hex ? "HEX" : "STR";

    public string DisplayDensityLabel => DisplayDensity switch
    {
        MessageDisplayDensity.Plain => L["stream.density.plain"],
        MessageDisplayDensity.Slim => L["stream.density.slim"],
        _ => L["stream.density.detailed"]
    };

    public string DataSourceLabel => DataSource switch
    {
        MessageFrameDataSource.Archive => L["stream.source.history"],
        _ => L["stream.source.live"]
    };

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

    public void ToggleDisplayMode()
        => PayloadRenderMode = PayloadRenderMode == PayloadRenderMode.Hex
            ? PayloadRenderMode.String
            : PayloadRenderMode.Hex;

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

                MessageItems.Add(CreateItemContext(message));
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

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            LoadSearchResults();
            return;
        }

        var result = _messageFrameQuery.Query(new MessageFrameQuery(
            _activeSession.Id,
            DataSource,
            MessageFrameQueryKind.Latest,
            0,
            _settingsService.Current.Display.MaxMessages));

        foreach (var frame in result.Frames)
        {
            MessageItems.Add(CreateItemContext(frame));
        }
    }

    private void ApplyFilter() => LoadMessages();

    private void LoadSearchResults()
    {
        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        var filtered = _messageStream.Search(_activeSession.Id, _searchQuery);
        foreach (var message in filtered)
        {
            MessageItems.Add(CreateItemContext(message));
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

    private void RefreshPayloadRenderMode()
    {
        foreach (var item in MessageItems)
        {
            item.UpdatePayloadRenderMode(PayloadRenderMode);
        }
    }

    private void RefreshDisplayDensity()
    {
        foreach (var item in MessageItems)
        {
            item.UpdateDisplayDensity(DisplayDensity);
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

    private LogMessageListItemContext CreateItemContext(MessageFrameRecord frame)
        => CreateItemContext(new LogMessage
        {
            Id = frame.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Timestamp = frame.TimestampUtc,
            Content = FormatContent(frame.RawData, frame.Format),
            Level = LogLevel.Info,
            Source = frame.Direction == FrameDirection.Tx ? "TX" : "RX",
            RawData = frame.RawData,
            Format = frame.Format,
            Attributes = frame.Attributes,
            AttributeSchemaVersion = frame.AttributeSchemaVersion
        });

    private LogMessageListItemContext CreateItemContext(LogMessage message)
        => new(message, Display.TimestampFormat, PayloadRenderMode, DisplayDensity);

    private static string FormatContent(byte[] rawData, MessageFormat format)
    {
        if (rawData.Length == 0)
        {
            return string.Empty;
        }

        return format == MessageFormat.Hex
            ? BitConverter.ToString(rawData).Replace("-", " ")
            : System.Text.Encoding.UTF8.GetString(rawData);
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
