using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed record MessageDisplayDensityOption(MessageDisplayDensity Density, string Label);

public sealed record MessageDataSourceOption(MessageFrameDataSource Source, string Label);

public sealed class MessageStreamViewModel : BaseViewModel
{
    private sealed record MessageWindowSnapshot(
        MessageFrameDataSource Source,
        long FirstFrameId,
        long LastFrameId,
        int Limit);

    private readonly IMessageStreamService _messageStream;
    private readonly IMessageFrameQueryService _messageFrameQuery;
    private readonly IMessageFrameSearchService _messageFrameSearch;
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
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
    private IReadOnlyList<MessageFrameSearchMatch> _searchMatches = Array.Empty<MessageFrameSearchMatch>();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _searchDebounceCts;
    private readonly Dictionary<long, (int Start, int Length)> _aggregateFrameRanges = new();
    private MessageWindowSnapshot? _currentWindowSnapshot;
    private MessageWindowSnapshot? _preSearchWindowSnapshot;
    private int _selectedSearchMatchIndex = -1;
    private int _aggregateSelectionStart;
    private int _aggregateSelectionLength;
    private int _aggregateSelectionVersion;
    private int _selectedMessageNavigationVersion;
    private int _returnToLatestNavigationVersion;
    private bool _applyingSessionDisplayOptions;
    private bool _isMetricsBarVisible;
    private bool _isSearchRunning;
    private bool _isReturnToLatestVisible;
    private string _searchStatus = string.Empty;
    private string _aggregateMessageText = string.Empty;
    private LogMessageListItemViewModel? _selectedMessageItem;

    public MessageStreamViewModel(
        ILocalizationService localization,
        IMessageStreamService messageStream,
        IMessageFrameQueryService messageFrameQuery,
        IMessageFrameSearchService messageFrameSearch,
        IWorkspaceCoordinator workspaceCoordinator,
        SettingsService settingsService,
        DisplaySettingsViewModel display,
        IItemVmFactory<LogMessageListItemViewModel, LogMessageListItemContext> itemFactory)
        : base(localization)
    {
        _messageStream = messageStream;
        _messageFrameQuery = messageFrameQuery;
        _messageFrameSearch = messageFrameSearch;
        _workspaceCoordinator = workspaceCoordinator;
        _settingsService = settingsService;
        Display = display;
        _itemFactory = itemFactory;
        _activeSessionPropertyChangedHandler = OnActiveSessionPropertyChanged;
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

    public IReadOnlyList<MessageDataSourceOption> DataSourceOptions =>
        CanOpenArchiveHistory
            ?
            [
                new(MessageFrameDataSource.LiveSpool, L["stream.source.live"]),
                new(MessageFrameDataSource.Archive, L["stream.source.history"])
            ]
            :
            [
                new(MessageFrameDataSource.LiveSpool, L["stream.source.live"])
            ];

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
            var hadSearch = !string.IsNullOrWhiteSpace(_searchQuery);
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            if (!hadSearch && !string.IsNullOrWhiteSpace(_searchQuery))
            {
                _preSearchWindowSnapshot = CaptureCurrentWindowSnapshot();
                IsReturnToLatestVisible = false;
            }

            ResetSearchState();
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                if (hadSearch)
                {
                    RestorePreSearchWindow();
                }
                else
                {
                    LoadMessages();
                }

                return;
            }

            ScheduleSearch();
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
                OnPropertyChanged(nameof(IsPayloadHexMode));
                OnPropertyChanged(nameof(IsAggregateTextMode));
                OnPropertyChanged(nameof(IsDetailedDisplayMode));
                RefreshPayloadRenderMode();
                PersistSessionDisplayOptions();
            }
        }
    }

    public bool IsPayloadHexMode
    {
        get => PayloadRenderMode == PayloadRenderMode.Hex;
        set => PayloadRenderMode = value ? PayloadRenderMode.Hex : PayloadRenderMode.String;
    }

    public MessageDisplayDensity DisplayDensity
    {
        get => _displayDensity;
        set
        {
            if (SetProperty(ref _displayDensity, value))
            {
                OnPropertyChanged(nameof(DisplayDensityLabel));
                OnPropertyChanged(nameof(IsAggregateTextMode));
                OnPropertyChanged(nameof(IsDetailedDisplayMode));
                _selectedDisplayDensityOption = DisplayDensityOptions.FirstOrDefault(x => x.Density == value);
                OnPropertyChanged(nameof(SelectedDisplayDensityOption));
                RefreshDisplayDensity();
                PersistSessionDisplayOptions();
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
                OnPropertyChanged(nameof(SelectedDataSourceOption));
                OnPropertyChanged(nameof(IsArchiveHistoryMode));
                OnPropertyChanged(nameof(CanOpenLiveData));
                _preSearchWindowSnapshot = null;
                IsReturnToLatestVisible = false;
                ResetSearchState();
                LoadMessages();
            }
        }
    }

    public MessageDataSourceOption? SelectedDataSourceOption
    {
        get => DataSourceOptions.FirstOrDefault(x => x.Source == DataSource);
        set
        {
            if (value is null || value.Source == DataSource)
            {
                return;
            }

            if (value.Source == MessageFrameDataSource.Archive && !CanOpenArchiveHistory)
            {
                OnPropertyChanged();
                return;
            }

            DataSource = value.Source;
        }
    }

    public bool IsMetricsBarVisible
    {
        get => _isMetricsBarVisible;
        set
        {
            if (SetProperty(ref _isMetricsBarVisible, value))
            {
                OnPropertyChanged(nameof(IsMessageStatusBarVisible));
            }
        }
    }

    public LogMessageListItemViewModel? SelectedMessageItem
    {
        get => _selectedMessageItem;
        set => SetProperty(ref _selectedMessageItem, value);
    }

    public bool IsSearchRunning
    {
        get => _isSearchRunning;
        private set
        {
            if (SetProperty(ref _isSearchRunning, value))
            {
                OnPropertyChanged(nameof(CanStartSearch));
                OnPropertyChanged(nameof(CanCancelSearch));
            }
        }
    }

    public string SearchStatus
    {
        get => _searchStatus;
        private set
        {
            if (SetProperty(ref _searchStatus, value))
            {
                OnPropertyChanged(nameof(HasSearchStatus));
                OnPropertyChanged(nameof(IsMessageStatusBarVisible));
            }
        }
    }

    public bool HasSearchStatus => !string.IsNullOrWhiteSpace(SearchStatus);

    public bool IsMessageStatusBarVisible => IsMetricsBarVisible || HasSearchStatus;

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool IsReturnToLatestVisible
    {
        get => _isReturnToLatestVisible;
        private set => SetProperty(ref _isReturnToLatestVisible, value);
    }

    public string AggregateMessageText
    {
        get => _aggregateMessageText;
        private set => SetProperty(ref _aggregateMessageText, value);
    }

    public bool IsAggregateTextMode => DisplayDensity is MessageDisplayDensity.Plain or MessageDisplayDensity.Slim;

    public bool IsDetailedDisplayMode => DisplayDensity == MessageDisplayDensity.Detailed;

    public int AggregateSelectionStart => _aggregateSelectionStart;

    public int AggregateSelectionLength => _aggregateSelectionLength;

    public int AggregateSelectionVersion => _aggregateSelectionVersion;

    public int SelectedMessageNavigationVersion => _selectedMessageNavigationVersion;

    public int ReturnToLatestNavigationVersion => _returnToLatestNavigationVersion;

    public int SearchMatchCount => _searchMatches.Count;

    public int CurrentSearchMatchOrdinal => _selectedSearchMatchIndex >= 0 ? _selectedSearchMatchIndex + 1 : 0;

    public bool HasSearchMatches => _searchMatches.Count > 0;

    public string SearchNavigationStatus => HasSearchMatches ? FormatSearchPosition() : string.Empty;

    public bool CanStartSearch => !IsSearchRunning
                                  && _activeSession is not null
                                  && !string.IsNullOrWhiteSpace(SearchQuery);

    public bool CanCancelSearch => IsSearchRunning;

    public bool CanNavigateSearch => !IsSearchRunning && HasSearchMatches;

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

    public string ArchiveStatusLabel => _activeSession?.ArchiveState switch
    {
        SessionArchiveState.Enabled => L["session.archive.status.enabled"],
        SessionArchiveState.Stopped => L["session.archive.status.stopped"],
        SessionArchiveState.Error => L["session.archive.status.error"],
        _ => L["session.archive.status.disabled"]
    };

    public string ArchiveStatusTooltip => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        L["session.archive.tooltip"],
        ArchiveStatusLabel);

    public bool CanEnableArchive => _activeSession is { ArchiveState: SessionArchiveState.Disabled };

    public bool CanStopArchive => _activeSession is { ArchiveState: SessionArchiveState.Enabled };

    public bool CanOpenArchiveHistory => _activeSession?.ArchiveState is SessionArchiveState.Enabled or SessionArchiveState.Stopped or SessionArchiveState.Error;

    public bool IsArchiveHistoryMode => DataSource == MessageFrameDataSource.Archive;

    public bool CanOpenLiveData => IsArchiveHistoryMode;

    public bool IsArchiveWriting => _activeSession?.ArchiveState == SessionArchiveState.Enabled;

    public bool CanToggleArchiveWriting => _activeSession is not null;

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

    public void ClearSearch()
        => SearchQuery = string.Empty;

    public void ReturnToLatest()
    {
        IsReturnToLatestVisible = false;
        _preSearchWindowSnapshot = null;
        LoadMessages();
        _returnToLatestNavigationVersion++;
        OnPropertyChanged(nameof(ReturnToLatestNavigationVersion));
    }

    public async Task StartSearchAsync()
    {
        if (_activeSession?.Id is not { Length: > 0 } sessionId || string.IsNullOrWhiteSpace(SearchQuery))
        {
            ResetSearchState();
            LoadMessages();
            return;
        }

        CancelSearch();
        var cts = new CancellationTokenSource();
        var source = DataSource;
        var text = SearchQuery;
        _searchCts = cts;
        IsSearchRunning = true;
        SearchStatus = L["stream.search.running"];
        _searchMatches = Array.Empty<MessageFrameSearchMatch>();
        _selectedSearchMatchIndex = -1;
        RaiseSearchStateChanged();

        try
        {
            var result = await Task.Run(
                () => _messageFrameSearch.SearchAsync(new MessageFrameSearchQuery(
                    sessionId,
                    source,
                    text,
                    null), cts.Token),
                cts.Token);

            if (!IsCurrentSearch(cts, sessionId, source, text))
            {
                return;
            }

            _searchMatches = result.Matches;
            _selectedSearchMatchIndex = _searchMatches.Count > 0 ? 0 : -1;
            SearchStatus = _searchMatches.Count == 0
                ? L["stream.search.noResults"]
                : FormatSearchPosition();
            RaiseSearchStateChanged();

            if (_selectedSearchMatchIndex >= 0)
            {
                LoadAroundFrame(_searchMatches[_selectedSearchMatchIndex].FrameId);
            }
            else
            {
                UpdateDetailedSearchMatchState();
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                SearchStatus = L["stream.search.cancelled"];
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearchRunning = false;
                _searchCts = null;
                RaiseSearchStateChanged();
            }

            cts.Dispose();
        }
    }

    public void CancelSearch()
        => _searchCts?.Cancel();

    public void GoToNextSearchMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _selectedSearchMatchIndex = (_selectedSearchMatchIndex + 1) % _searchMatches.Count;
        SearchStatus = FormatSearchPosition();
        RaiseSearchStateChanged();
        LoadAroundFrame(_searchMatches[_selectedSearchMatchIndex].FrameId);
    }

    public void GoToPreviousSearchMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _selectedSearchMatchIndex = _selectedSearchMatchIndex <= 0
            ? _searchMatches.Count - 1
            : _selectedSearchMatchIndex - 1;
        SearchStatus = FormatSearchPosition();
        RaiseSearchStateChanged();
        LoadAroundFrame(_searchMatches[_selectedSearchMatchIndex].FrameId);
    }

    public async Task EnableArchiveAsync()
    {
        if (_activeSession?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        await _workspaceCoordinator.SetSessionArchiveStateAsync(sessionId, SessionArchiveState.Enabled);
    }

    public async Task StopArchiveAsync()
    {
        if (_activeSession?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        await _workspaceCoordinator.SetSessionArchiveStateAsync(sessionId, SessionArchiveState.Stopped);
    }

    public Task SetArchiveWritingAsync(bool enabled)
        => enabled ? EnableArchiveAsync() : StopArchiveAsync();

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
        ApplySessionDisplayOptions(session);
        RaiseSessionContextChanged();
        _currentWindowSnapshot = null;
        _preSearchWindowSnapshot = null;
        IsReturnToLatestVisible = false;

        _messageSubscription?.Dispose();
        _messageSubscription = null;

        LoadMessages();

        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        _activeSession.PropertyChanged += _activeSessionPropertyChangedHandler;

        var sessionId = _activeSession.Id;
        _messageSubscription = _messageStream.Subscribe(sessionId, _ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeSession?.Id != sessionId)
                {
                    return;
                }

                if (DataSource == MessageFrameDataSource.LiveSpool
                    && string.IsNullOrWhiteSpace(SearchQuery)
                    && !IsReturnToLatestVisible)
                {
                    LoadMessages();
                }
            });
        });
    }

    public void ClearView()
    {
        MessageItems.Clear();
        _currentWindowSnapshot = null;
        _preSearchWindowSnapshot = null;
        IsReturnToLatestVisible = false;
        RebuildAggregateText();
    }

    private void LoadMessages()
    {
        MessageItems.Clear();
        SelectedMessageItem = null;

        if (_activeSession?.Id is not { Length: > 0 })
        {
            _currentWindowSnapshot = null;
            RebuildAggregateText();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            RebuildAggregateText();
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

        UpdateCurrentWindowSnapshot();
        RebuildAggregateText();
    }

    private void LoadAroundFrame(long frameId)
    {
        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        MessageItems.Clear();
        SelectedMessageItem = null;

        var max = Math.Max(1, _settingsService.Current.Display.MaxMessages);
        var beforeCount = max / 2;
        var afterCount = max - beforeCount;
        var before = beforeCount > 0
            ? _messageFrameQuery.Query(new MessageFrameQuery(
                _activeSession.Id,
                DataSource,
                MessageFrameQueryKind.Before,
                frameId,
                beforeCount)).Frames
            : Array.Empty<MessageFrameRecord>();
        var after = _messageFrameQuery.Query(new MessageFrameQuery(
            _activeSession.Id,
            DataSource,
            MessageFrameQueryKind.After,
            frameId - 1,
            afterCount)).Frames;

        foreach (var frame in before.Concat(after).GroupBy(frame => frame.FrameId).Select(group => group.First()).OrderBy(frame => frame.FrameId))
        {
            var item = MessageItems.Add(CreateItemContext(frame));
            if (frame.FrameId == frameId)
            {
                SelectedMessageItem = item;
                _selectedMessageNavigationVersion++;
                OnPropertyChanged(nameof(SelectedMessageNavigationVersion));
            }
        }

        UpdateCurrentWindowSnapshot();
        RebuildAggregateText();
        SelectAggregateFrame(frameId);
        UpdateDetailedSearchMatchState();
    }

    private void RestorePreSearchWindow()
    {
        var snapshot = _preSearchWindowSnapshot;
        _preSearchWindowSnapshot = null;

        if (snapshot is null || _activeSession?.Id is not { Length: > 0 })
        {
            LoadMessages();
            return;
        }

        var shouldSuspendFollowLatest = DataSource == MessageFrameDataSource.LiveSpool;
        IsReturnToLatestVisible = shouldSuspendFollowLatest;
        var restored = RestoreWindow(snapshot);
        IsReturnToLatestVisible = restored && shouldSuspendFollowLatest;
    }

    private bool RestoreWindow(MessageWindowSnapshot snapshot)
    {
        if (_activeSession?.Id is not { Length: > 0 } || snapshot.Source != DataSource)
        {
            LoadMessages();
            return false;
        }

        MessageItems.Clear();
        SelectedMessageItem = null;

        var result = _messageFrameQuery.Query(new MessageFrameQuery(
            _activeSession.Id,
            snapshot.Source,
            MessageFrameQueryKind.After,
            Math.Max(0, snapshot.FirstFrameId - 1),
            Math.Max(1, snapshot.Limit)));

        foreach (var frame in result.Frames)
        {
            if (frame.FrameId <= snapshot.LastFrameId)
            {
                MessageItems.Add(CreateItemContext(frame));
            }
        }

        if (MessageItems.Count == 0)
        {
            LoadMessages();
            return false;
        }

        UpdateCurrentWindowSnapshot();
        RebuildAggregateText();
        return true;
    }

    private MessageWindowSnapshot? CaptureCurrentWindowSnapshot()
    {
        var first = GetFrameId(MessageItems.FirstOrDefault());
        var last = GetFrameId(MessageItems.LastOrDefault());
        if (first <= 0 || last <= 0)
        {
            return _currentWindowSnapshot;
        }

        return new MessageWindowSnapshot(
            DataSource,
            first,
            last,
            Math.Max(1, MessageItems.Count));
    }

    private void UpdateCurrentWindowSnapshot()
    {
        var first = GetFrameId(MessageItems.FirstOrDefault());
        var last = GetFrameId(MessageItems.LastOrDefault());
        _currentWindowSnapshot = first > 0 && last > 0
            ? new MessageWindowSnapshot(DataSource, first, last, Math.Max(1, MessageItems.Count))
            : null;
    }

    private static long GetFrameId(LogMessageListItemViewModel? item)
        => item is not null
           && long.TryParse(item.Message.Id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var frameId)
            ? frameId
            : 0;

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
            or nameof(Session.ArchiveState)
            or nameof(Session.ArchiveError)
            or nameof(Session.PayloadRenderMode)
            or nameof(Session.DisplayDensity)
            or null
            or "")
        {
            if (e.PropertyName is nameof(Session.PayloadRenderMode) or nameof(Session.DisplayDensity) or null or "")
            {
                ApplySessionDisplayOptions(_activeSession);
            }

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
        OnPropertyChanged(nameof(ArchiveStatusLabel));
        OnPropertyChanged(nameof(ArchiveStatusTooltip));
        OnPropertyChanged(nameof(DataSourceOptions));
        OnPropertyChanged(nameof(SelectedDataSourceOption));
        OnPropertyChanged(nameof(CanEnableArchive));
        OnPropertyChanged(nameof(CanStopArchive));
        OnPropertyChanged(nameof(CanOpenArchiveHistory));
        OnPropertyChanged(nameof(IsArchiveHistoryMode));
        OnPropertyChanged(nameof(CanOpenLiveData));
        OnPropertyChanged(nameof(IsArchiveWriting));
        OnPropertyChanged(nameof(CanToggleArchiveWriting));
    }

    private void RefreshPayloadRenderMode()
    {
        foreach (var item in MessageItems)
        {
            item.UpdatePayloadRenderMode(PayloadRenderMode);
        }

        RebuildAggregateText();
    }

    private void RefreshDisplayDensity()
    {
        foreach (var item in MessageItems)
        {
            item.UpdateDisplayDensity(DisplayDensity);
        }

        RebuildAggregateText();
    }

    private void TrimMessages()
    {
        var max = _settingsService.Current.Display.MaxMessages;
        while (MessageItems.Count > max)
        {
            MessageItems.RemoveAt(0);
        }
    }

    private void ResetSearchState()
    {
        CancelSearchDebounce();
        AbandonSearch();
        _searchMatches = Array.Empty<MessageFrameSearchMatch>();
        _selectedSearchMatchIndex = -1;
        SearchStatus = string.Empty;
        ClearAggregateSelection();
        UpdateDetailedSearchMatchState();
        RaiseSearchStateChanged();
    }

    private void RaiseSearchStateChanged()
    {
        OnPropertyChanged(nameof(SearchMatchCount));
        OnPropertyChanged(nameof(CurrentSearchMatchOrdinal));
        OnPropertyChanged(nameof(HasSearchMatches));
        OnPropertyChanged(nameof(SearchNavigationStatus));
        OnPropertyChanged(nameof(CanStartSearch));
        OnPropertyChanged(nameof(CanCancelSearch));
        OnPropertyChanged(nameof(CanNavigateSearch));
        OnPropertyChanged(nameof(HasSearchQuery));
    }

    private string FormatSearchPosition()
        => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            L["stream.search.resultPosition"],
            CurrentSearchMatchOrdinal,
            SearchMatchCount);

    private bool IsCurrentSearch(
        CancellationTokenSource cts,
        string sessionId,
        MessageFrameDataSource source,
        string text)
        => ReferenceEquals(_searchCts, cts)
           && string.Equals(_activeSession?.Id, sessionId, StringComparison.Ordinal)
           && DataSource == source
           && string.Equals(SearchQuery, text, StringComparison.Ordinal);

    private void AbandonSearch()
    {
        var cts = _searchCts;
        if (cts is null)
        {
            return;
        }

        _searchCts = null;
        cts.Cancel();
        IsSearchRunning = false;
    }

    private void ScheduleSearch()
    {
        CancelSearchDebounce();
        _searchDebounceCts = new CancellationTokenSource();
        var cts = _searchDebounceCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token);
                Dispatcher.UIThread.Post(async () =>
                {
                    if (!cts.IsCancellationRequested)
                    {
                        await StartSearchAsync();
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_searchDebounceCts, cts))
                {
                    _searchDebounceCts = null;
                }

                cts.Dispose();
            }
        });
    }

    private void CancelSearchDebounce()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = null;
    }

    private void RebuildAggregateText()
    {
        _aggregateFrameRanges.Clear();

        if (!IsAggregateTextMode)
        {
            AggregateMessageText = string.Empty;
            ClearAggregateSelection();
            return;
        }

        var sb = new StringBuilder();
        foreach (var item in MessageItems)
        {
            var frameId = long.TryParse(item.Message.Id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
            var start = sb.Length;

            if (DisplayDensity == MessageDisplayDensity.Slim)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(item.Source);
                sb.Append("  ");
            }

            sb.Append(RenderAggregatePayload(item.Message));

            if (frameId > 0)
            {
                _aggregateFrameRanges[frameId] = (start, sb.Length - start);
            }
        }

        AggregateMessageText = sb.ToString();
    }

    private void SelectAggregateFrame(long frameId)
    {
        if (!_aggregateFrameRanges.TryGetValue(frameId, out var range))
        {
            ClearAggregateSelection();
            return;
        }

        _aggregateSelectionStart = range.Start;
        _aggregateSelectionLength = Math.Max(0, range.Length);
        _aggregateSelectionVersion++;
        OnPropertyChanged(nameof(AggregateSelectionStart));
        OnPropertyChanged(nameof(AggregateSelectionLength));
        OnPropertyChanged(nameof(AggregateSelectionVersion));
    }

    private void ClearAggregateSelection()
    {
        _aggregateSelectionStart = 0;
        _aggregateSelectionLength = 0;
        _aggregateSelectionVersion++;
        OnPropertyChanged(nameof(AggregateSelectionStart));
        OnPropertyChanged(nameof(AggregateSelectionLength));
        OnPropertyChanged(nameof(AggregateSelectionVersion));
    }

    private void UpdateDetailedSearchMatchState()
    {
        var matchedFrameIds = _searchMatches
            .Select(match => match.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        var currentFrameId = _selectedSearchMatchIndex >= 0 && _selectedSearchMatchIndex < _searchMatches.Count
            ? _searchMatches[_selectedSearchMatchIndex].FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        foreach (var item in MessageItems)
        {
            var frameId = item.Message.Id;
            item.UpdateSearchMatchState(
                matchedFrameIds.Contains(frameId),
                string.Equals(frameId, currentFrameId, StringComparison.Ordinal));
        }
    }

    private string RenderAggregatePayload(LogMessage message)
    {
        var raw = message.RawData;
        if (raw is null || raw.Length == 0)
        {
            return message.Content ?? string.Empty;
        }

        return PayloadRenderMode == PayloadRenderMode.Hex
            ? BitConverter.ToString(raw).Replace("-", " ")
            : Encoding.UTF8.GetString(raw);
    }

    private void ApplySessionDisplayOptions(Session? session)
    {
        _applyingSessionDisplayOptions = true;
        try
        {
            PayloadRenderMode = session?.PayloadRenderMode ?? PayloadRenderMode.String;
            DisplayDensity = session?.DisplayDensity ?? MessageDisplayDensity.Detailed;
        }
        finally
        {
            _applyingSessionDisplayOptions = false;
        }
    }

    private void PersistSessionDisplayOptions()
    {
        if (_applyingSessionDisplayOptions || _activeSession?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        _ = _workspaceCoordinator.SetSessionDisplayOptionsAsync(sessionId, PayloadRenderMode, DisplayDensity);
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
            AbandonSearch();
            CancelSearchDebounce();
            MessageItems.Dispose();
        }

        base.Dispose(disposing);
    }
}
