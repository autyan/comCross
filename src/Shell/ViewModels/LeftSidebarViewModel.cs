using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shell.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public enum LeftSidebarSection
{
    QuickCreate,
    Sessions
}

public enum LeftSidebarSessionSurfaceMode
{
    Status,
    ReconnectParameters
}

public sealed class LeftSidebarViewModel : BaseViewModel
{
    private const int ChildPreviewLimit = 5;

    private readonly IEventBus _eventBus;
    private readonly WorkloadService _workloadService;
    private readonly PluginUiStateManager _pluginUiStateManager;
    private readonly PluginManagerViewModel _pluginManager;
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly BusAdapterSelectorViewModel _busAdapterSelectorViewModel;
    private readonly IObjectFactory _objectFactory;
    private readonly IItemVmFactory<SessionListItemViewModel, Session> _sessionItemFactory;

    private readonly IDisposable _sessionCreatedSubscription;
    private readonly IDisposable _sessionUpdatedSubscription;
    private readonly IDisposable _sessionClosedSubscription;
    private readonly IDisposable _sessionDeletedSubscription;
    private readonly IDisposable _workloadSessionMembershipChangedSubscription;
    private readonly IDisposable _activeWorkloadChangedSubscription;
    private readonly EventHandler _pluginsReloadedHandler;

    private readonly PropertyChangedEventHandler _activeSessionPropertyChangedHandler;

    // Simplified parent-child UI: allocate stable connection indices for child sessions.
    private readonly Dictionary<string, int> _connectionIndexBySessionId = new(StringComparer.Ordinal);
    private int _nextConnectionIndex;

    // UI-only state: collapsed parent nodes.
    private readonly HashSet<string> _collapsedParentSessionIds = new(StringComparer.Ordinal);

    private Session? _activeSession;
    private bool _isConnected;
    private string? _preferredActiveSessionId;
    private SessionListItemViewModel? _selectedSessionItem;
    private bool _syncingSelection;
    private HashSet<string> _visibleSessionIds = new(StringComparer.Ordinal);
    private LeftSidebarSection _activeSection = LeftSidebarSection.Sessions;
    private LeftSidebarSessionSurfaceMode _sessionSurfaceMode = LeftSidebarSessionSurfaceMode.Status;

    public LeftSidebarViewModel(
        ILocalizationService localization,
        IEventBus eventBus,
        WorkloadService workloadService,
        PluginUiStateManager pluginUiStateManager,
        PluginManagerViewModel pluginManager,
        IWorkspaceCoordinator workspaceCoordinator,
        BusAdapterSelectorViewModel busAdapterSelectorViewModel,
        IObjectFactory objectFactory,
        IItemVmFactory<SessionListItemViewModel, Session> sessionItemFactory)
        : base(localization)
    {
        _eventBus = eventBus;
        _workloadService = workloadService;
        _pluginUiStateManager = pluginUiStateManager;
        _pluginManager = pluginManager;
        _workspaceCoordinator = workspaceCoordinator;
        _busAdapterSelectorViewModel = busAdapterSelectorViewModel;
        _objectFactory = objectFactory;
        _sessionItemFactory = sessionItemFactory;

        _sessionCreatedSubscription = _eventBus.Subscribe<SessionCreatedEvent>(OnSessionCreated);
        _sessionUpdatedSubscription = _eventBus.Subscribe<SessionUpdatedEvent>(OnSessionUpdated);
        _sessionClosedSubscription = _eventBus.Subscribe<SessionClosedEvent>(OnSessionClosed);
        _sessionDeletedSubscription = _eventBus.Subscribe<SessionDeletedEvent>(OnSessionDeleted);
        _workloadSessionMembershipChangedSubscription = _eventBus.Subscribe<WorkloadSessionMembershipChangedEvent>(OnWorkloadSessionMembershipChanged);
        _activeWorkloadChangedSubscription = _eventBus.Subscribe<ActiveWorkloadChangedEvent>(OnActiveWorkloadChanged);
        SessionItems = new ItemVmCollection<SessionListItemViewModel, Session>(_sessionItemFactory);
        QuickCreateSelectorViewModel = _objectFactory.Create<BusAdapterSelectorViewModel>(
            BusAdapterSelectorViewModel.BusAdapterViewKind,
            $"left-quick-create-{Guid.NewGuid():N}");
        ReconnectEditorSelectorViewModel = _objectFactory.Create<BusAdapterSelectorViewModel>(
            BusAdapterSelectorViewModel.BusAdapterViewKind,
            $"left-reconnect-{Guid.NewGuid():N}");
        RefreshQuickCreateAdapters();
        ReconnectEditorSelectorViewModel.UpdatePluginAdapters(_pluginManager.GetAllCapabilityOptions());
        _pluginsReloadedHandler = (_, _) =>
        {
            RefreshQuickCreateAdapters();
            ReconnectEditorSelectorViewModel.UpdatePluginAdapters(_pluginManager.GetAllCapabilityOptions());
        };
        _pluginManager.PluginsReloaded += _pluginsReloadedHandler;
        ShowQuickCreateSectionCommand = new RelayCommand(() => ActiveSection = LeftSidebarSection.QuickCreate);
        ShowSessionsSectionCommand = new RelayCommand(() => ActiveSection = LeftSidebarSection.Sessions);
        ShowReconnectParametersSurfaceCommand = new RelayCommand(() => SessionSurfaceMode = LeftSidebarSessionSurfaceMode.ReconnectParameters);
        ShowSessionStatusSurfaceCommand = new RelayCommand(() => SessionSurfaceMode = LeftSidebarSessionSurfaceMode.Status);
        DirectReconnectCommand = new AsyncRelayCommand(DirectReconnectAsync, () => CanReconnectActiveSession);

        _activeSessionPropertyChangedHandler = (_, args) =>
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => HandleActiveSessionPropertyChanged(args.PropertyName));
                return;
            }

            HandleActiveSessionPropertyChanged(args.PropertyName);
        };
    }

    private void HandleActiveSessionPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(Session.Status)
            || propertyName == nameof(Session.CanReconnect)
            || propertyName == nameof(Session.InitializationState)
            || string.IsNullOrEmpty(propertyName))
        {
            IsConnected = _activeSession?.Status == SessionStatus.Connected;
            SyncReconnectSurface();
        }

        if (propertyName is nameof(Session.Name) or nameof(Session.DisplayTitle) or "" or null)
        {
            OnPropertyChanged(nameof(CurrentSessionName));
        }

        if (propertyName is nameof(Session.Endpoint) or nameof(Session.DisplaySubtitle) or "" or null)
        {
            OnPropertyChanged(nameof(CurrentSessionEndpoint));
        }

        if (propertyName is nameof(Session.RxBytes) or "" or null)
        {
            OnPropertyChanged(nameof(CurrentSessionRxBytes));
        }

        if (propertyName is nameof(Session.TxBytes) or "" or null)
        {
            OnPropertyChanged(nameof(CurrentSessionTxBytes));
        }
    }

    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel => _busAdapterSelectorViewModel;

    public BusAdapterSelectorViewModel QuickCreateSelectorViewModel { get; }

    public BusAdapterSelectorViewModel ReconnectEditorSelectorViewModel { get; }

    public RelayCommand ShowQuickCreateSectionCommand { get; }

    public RelayCommand ShowSessionsSectionCommand { get; }

    public RelayCommand ShowReconnectParametersSurfaceCommand { get; }

    public RelayCommand ShowSessionStatusSurfaceCommand { get; }

    public AsyncRelayCommand DirectReconnectCommand { get; }

    public ObservableCollection<Session> Sessions { get; } = new();

    // UI-only collection for typed, compiled bindings.
    public ItemVmCollection<SessionListItemViewModel, Session> SessionItems { get; }

    public LeftSidebarSection ActiveSection
    {
        get => _activeSection;
        set
        {
            if (SetProperty(ref _activeSection, value))
            {
                if (_activeSection == LeftSidebarSection.QuickCreate)
                {
                    RefreshQuickCreateAdapters();
                }

                OnPropertyChanged(nameof(IsQuickCreateSection));
                OnPropertyChanged(nameof(IsSessionsSection));
            }
        }
    }

    public bool IsQuickCreateSection => ActiveSection == LeftSidebarSection.QuickCreate;

    public bool IsSessionsSection => ActiveSection == LeftSidebarSection.Sessions;

    public bool HasActiveSession => _activeSession is not null;

    public LeftSidebarSessionSurfaceMode SessionSurfaceMode
    {
        get => _sessionSurfaceMode;
        set
        {
            if (SetProperty(ref _sessionSurfaceMode, value))
            {
                OnPropertyChanged(nameof(ShowSessionStatusSurface));
                OnPropertyChanged(nameof(ShowReconnectParametersSurface));
            }
        }
    }

    public bool ShowSessionStatusSurface => SessionSurfaceMode == LeftSidebarSessionSurfaceMode.Status;

    public bool ShowReconnectParametersSurface => SessionSurfaceMode == LeftSidebarSessionSurfaceMode.ReconnectParameters;

    public bool CanReconnectActiveSession
        => _activeSession?.Id is { Length: > 0 }
           && _activeSession.InitializationState == SessionInitializationState.Ready
           && _activeSession.Status != SessionStatus.Connected
           && _activeSession.CanReconnect
           && !string.IsNullOrWhiteSpace(_activeSession.AdapterId);

    public bool CanShowConnectionParameters
        => _activeSession?.Id is { Length: > 0 }
           && _activeSession.InitializationState == SessionInitializationState.Ready
           && _activeSession.CanReconnect
           && !string.IsNullOrWhiteSpace(_activeSession.AdapterId);

    public string QuickCreateTabLabel => L["sidebar.tab.quickCreate"];

    public string SessionsTabLabel => L["sidebar.tab.sessions"];

    public string QuickCreateTitle => L["sidebar.quickCreate.title"];

    public string QuickCreateHint => L["sidebar.quickCreate.hint"];

    public string CurrentSessionTitle => L["sidebar.currentSession.title"];

    public string CurrentSessionEmptyTitle => L["sidebar.currentSession.empty"];

    public string CurrentSessionEmptyHint => L["sidebar.currentSession.emptyHint"];

    public string DirectReconnectLabel => L["sidebar.reconnect.direct"];

    public string EditReconnectLabel => L["sidebar.reconnect.edit"];

    public string BackToStatusLabel => L["sidebar.reconnect.back"];

    public string CurrentSessionName => ResolveSessionDisplayName(_activeSession);

    public string CurrentSessionEndpoint => _activeSession?.Endpoint ?? string.Empty;

    public string CurrentSessionRxBytes => $"{_activeSession?.RxBytes ?? 0:N0}";

    public string CurrentSessionTxBytes => $"{_activeSession?.TxBytes ?? 0:N0}";

    public SessionListItemViewModel? SelectedSessionItem
    {
        get => _selectedSessionItem;
        set
        {
            if (!SetProperty(ref _selectedSessionItem, value))
            {
                return;
            }
            if (_syncingSelection)
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                ActiveSession = value?.Session;
            }
            finally
            {
                _syncingSelection = false;
            }
        }
    }

    public Session? ActiveSession
    {
        get => _activeSession;
        set
        {
            var previous = _activeSession;
            if (previous != null)
            {
                previous.PropertyChanged -= _activeSessionPropertyChangedHandler;
            }

            if (!SetProperty(ref _activeSession, value))
            {
                if (previous != null)
                {
                    previous.PropertyChanged += _activeSessionPropertyChangedHandler;
                }
                return;
            }

            if (_activeSession != null)
            {
                _activeSession.PropertyChanged += _activeSessionPropertyChangedHandler;
            }

            if (!_syncingSelection)
            {
                _syncingSelection = true;
                try
                {
                    SelectedSessionItem = _activeSession is null
                        ? null
                        : SessionItems.FirstOrDefault(i => ReferenceEquals(i.Session, _activeSession));
                }
                finally
                {
                    _syncingSelection = false;
                }
            }

            // Sync UI context management (ADR-010 / Plugin UI v0.4.0)
            _pluginUiStateManager.SwitchContext(new PluginUiViewScope(BusAdapterSelectorViewModel.BusAdapterViewKind), _activeSession?.Id);
            _busAdapterSelectorViewModel.SetActiveSession(_activeSession);
            SessionSurfaceMode = LeftSidebarSessionSurfaceMode.Status;
            SyncReconnectSurface();

            IsConnected = _activeSession?.Status == SessionStatus.Connected;
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(CanReconnectActiveSession));
            OnPropertyChanged(nameof(CanShowConnectionParameters));
            OnPropertyChanged(nameof(CurrentSessionName));
            OnPropertyChanged(nameof(CurrentSessionEndpoint));
            OnPropertyChanged(nameof(CurrentSessionRxBytes));
            OnPropertyChanged(nameof(CurrentSessionTxBytes));
            DirectReconnectCommand.RaiseCanExecuteChanged();
            ActiveSessionChanged?.Invoke(this, _activeSession);
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public event EventHandler<Session?>? ActiveSessionChanged;

    public Task SyncToActiveWorkloadAsync() => RefreshVisibleSessionsForCurrentWorkloadAsync();

    private void RefreshQuickCreateAdapters()
    {
        QuickCreateSelectorViewModel.UpdatePluginAdapters(_pluginManager.GetAllCapabilityOptions());
    }

    private void SyncReconnectSurface()
    {
        var editableSession = _activeSession is not null
            && _activeSession.CanReconnect
            && !string.IsNullOrWhiteSpace(_activeSession.AdapterId)
            ? _activeSession
            : null;

        ReconnectEditorSelectorViewModel.PrepareReconnect(editableSession);

        if (editableSession is null)
        {
            SessionSurfaceMode = LeftSidebarSessionSurfaceMode.Status;
        }

        OnPropertyChanged(nameof(CanReconnectActiveSession));
        OnPropertyChanged(nameof(CanShowConnectionParameters));
        DirectReconnectCommand.RaiseCanExecuteChanged();
    }

    private async Task DirectReconnectAsync()
    {
        if (!CanReconnectActiveSession)
        {
            return;
        }

        await ReconnectEditorSelectorViewModel.ExecuteConnectAsync();

        if (_activeSession?.Status == SessionStatus.Connected || !CanReconnectActiveSession)
        {
            SessionSurfaceMode = LeftSidebarSessionSurfaceMode.Status;
        }
    }

    public void RefreshSessionItems()
    {
        // Sessions can be mutated by other VMs (e.g., delete). Ensure the derived UI list stays in sync.
        Dispatcher.UIThread.Post(RebuildSessionItems);
    }

    public void ToggleParentCollapsed(string parentSessionId)
    {
        if (string.IsNullOrWhiteSpace(parentSessionId))
        {
            return;
        }

        if (_collapsedParentSessionIds.Contains(parentSessionId))
        {
            _collapsedParentSessionIds.Remove(parentSessionId);
        }
        else
        {
            _collapsedParentSessionIds.Add(parentSessionId);
        }

        Dispatcher.UIThread.Post(RebuildSessionItems);
    }

    public void SetPreferredActiveSessionId(string? sessionId)
    {
        _preferredActiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
        TryApplyPreferredActiveSession();
    }

    public Task RenameSessionAsync(string sessionId, string name)
        => _workspaceCoordinator.RenameSessionAsync(sessionId, name);

    public Task DeleteSessionAsync(string sessionId)
        => _workspaceCoordinator.DeleteSessionAsync(sessionId);

    private void TryApplyPreferredActiveSession()
    {
        if (string.IsNullOrWhiteSpace(_preferredActiveSessionId))
        {
            return;
        }

        var preferred = Sessions.FirstOrDefault(s => string.Equals(s.Id, _preferredActiveSessionId, StringComparison.Ordinal));
        if (preferred is null)
        {
            // Preferred session is missing (deleted or stale persisted state).
            // Clear it so new sessions can become active by default.
            _preferredActiveSessionId = null;
            return;
        }

        ActiveSession = preferred;
        _preferredActiveSessionId = null;
    }

    private void OnActiveWorkloadChanged(ActiveWorkloadChangedEvent e)
    {
        _ = RefreshVisibleSessionsForCurrentWorkloadAsync();
    }

    private void OnWorkloadSessionMembershipChanged(WorkloadSessionMembershipChangedEvent e)
    {
        _ = RefreshVisibleSessionsForCurrentWorkloadAsync();
    }

    private async Task RefreshVisibleSessionsForCurrentWorkloadAsync()
    {
        var visibleSessionIds = await _workloadService.GetActiveWorkloadSessionIdsAsync();
        Dispatcher.UIThread.Post(() =>
        {
            _visibleSessionIds = new HashSet<string>(visibleSessionIds, StringComparer.Ordinal);
            RebuildSessionItems();

            if (ActiveSession is not null
                && (_visibleSessionIds.Count == 0 || _visibleSessionIds.Contains(ActiveSession.Id)))
            {
                return;
            }

            var nextVisibleSession = _visibleSessionIds.Count == 0
                ? Sessions.FirstOrDefault()
                : Sessions.FirstOrDefault(session => _visibleSessionIds.Contains(session.Id));
            ActiveSession = nextVisibleSession;
        });
    }

    private void OnSessionCreated(SessionCreatedEvent e)
        => UpsertSession(e.Session);

    private void OnSessionUpdated(SessionUpdatedEvent e)
        => UpsertSession(e.Session);

    private void UpsertSession(Session session)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (existing is null)
            {
                Sessions.Add(session);
                existing = session;
            }
            else
            {
                ApplySessionUpdate(existing, session);
            }

            EnsureConnectionIndex(existing);
            RebuildSessionItems();

            // Prefer restoring last active session (if provided); otherwise activate the newly created one.
            if (!string.IsNullOrWhiteSpace(_preferredActiveSessionId))
            {
                TryApplyPreferredActiveSession();

                // If the preferred session id was stale/missing (common because sessions are not restored on startup),
                // fall back to selecting the newly created session.
                if (ActiveSession is null)
                {
                    ActiveSession = existing;
                }
            }
            else if (!string.IsNullOrWhiteSpace(InferEffectiveParentSessionId(existing)) && ActiveSession is not null)
            {
                // New inbound child connections should not steal focus from the user's current listener/session selection.
                if (SelectedSessionItem is null && ActiveSession is not null)
                {
                    SelectedSessionItem = SessionItems.FirstOrDefault(i => ReferenceEquals(i.Session, ActiveSession));
                }
            }
            else
            {
                ActiveSession = existing;
            }

            ActiveSection = LeftSidebarSection.Sessions;
        });
    }

    private static void ApplySessionUpdate(Session target, Session source)
    {
        // Keep the existing Session instance to preserve bindings and list item viewmodels.
        target.Name = source.Name;
        target.AdapterId = source.AdapterId;
        target.PluginId = source.PluginId;
        target.CapabilityId = source.CapabilityId;
        target.ParametersJson = source.ParametersJson;
        target.DisplayTitle = source.DisplayTitle;
        target.DisplaySubtitle = source.DisplaySubtitle;
        target.DisplayIcon = source.DisplayIcon;
        target.CanReconnect = source.CanReconnect;
        target.InitializationState = source.InitializationState;
        target.InitializationError = source.InitializationError;
        target.EnableDatabaseStorage = source.EnableDatabaseStorage;
        target.ParentSessionId = source.ParentSessionId;
        target.ManagedResourceKinds = source.ManagedResourceKinds;
        target.Status = source.Status;
        target.StartTime = source.StartTime;
    }

    private void EnsureConnectionIndex(Session session)
    {
        EnsureConnectionIndex(session, session.ParentSessionId);
    }

    private void EnsureConnectionIndex(Session session, string? effectiveParentSessionId)
    {
        if (string.IsNullOrWhiteSpace(effectiveParentSessionId))
        {
            return;
        }

        if (_connectionIndexBySessionId.ContainsKey(session.Id))
        {
            return;
        }

        _nextConnectionIndex++;
        _connectionIndexBySessionId[session.Id] = _nextConnectionIndex;
    }

    private static string? InferEffectiveParentSessionId(Session session)
        => string.IsNullOrWhiteSpace(session.ParentSessionId) ? null : session.ParentSessionId;

    private void RebuildSessionItems()
    {
        // Preserve the current active session while we rebuild viewmodels.
        var activeSession = _activeSession;
        var visibleSessions = Sessions
            .Where(session => _visibleSessionIds.Count == 0 || _visibleSessionIds.Contains(session.Id))
            .ToList();

        // Keep connection index map bounded to current sessions.
        var aliveIds = visibleSessions.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _connectionIndexBySessionId.Keys.Where(k => !aliveIds.Contains(k)).ToList())
        {
            _connectionIndexBySessionId.Remove(key);
        }

        var effectiveParentBySessionId = visibleSessions.ToDictionary(
            s => s.Id,
            InferEffectiveParentSessionId,
            StringComparer.Ordinal);

        _syncingSelection = true;
        try
        {
            SelectedSessionItem = null;
            SessionItems.Clear();

            // Display order: top-level sessions first; children (connections) immediately after their listener.
            foreach (var top in visibleSessions.Where(s => string.IsNullOrWhiteSpace(effectiveParentBySessionId.GetValueOrDefault(s.Id))))
            {
                var topItem = SessionItems.Add(top);
                topItem.IndentLevel = 0;
                topItem.OverrideName = null;
                topItem.ChildSessionCount = visibleSessions.Count(s =>
                    string.Equals(effectiveParentBySessionId.GetValueOrDefault(s.Id), top.Id, StringComparison.Ordinal));

                topItem.IsCollapsed = topItem.HasChildSessions && _collapsedParentSessionIds.Contains(top.Id);

                if (topItem.HasChildSessions && topItem.IsCollapsed)
                {
                    continue;
                }

                var orderedChildren = visibleSessions
                    .Where(s => string.Equals(effectiveParentBySessionId.GetValueOrDefault(s.Id), top.Id, StringComparison.Ordinal))
                    .OrderBy(s => _connectionIndexBySessionId.TryGetValue(s.Id, out var idx) ? idx : int.MaxValue)
                    .ToList();

                var visibleChildIds = orderedChildren
                    .Take(ChildPreviewLimit)
                    .Select(s => s.Id)
                    .ToHashSet(StringComparer.Ordinal);

                if (activeSession is not null
                    && string.Equals(effectiveParentBySessionId.GetValueOrDefault(activeSession.Id), top.Id, StringComparison.Ordinal))
                {
                    visibleChildIds.Add(activeSession.Id);
                }

                foreach (var child in orderedChildren.Where(s => visibleChildIds.Contains(s.Id)))
                {
                    EnsureConnectionIndex(child, top.Id);
                    var idx = _connectionIndexBySessionId.TryGetValue(child.Id, out var assigned) ? assigned : 0;

                    var childItem = SessionItems.Add(child);
                    childItem.IndentLevel = 1;
                    childItem.OverrideName = IsDefaultInboundConnectionName(child)
                        ? idx > 0 ? $"Conn #{idx}" : "Conn"
                        : null;
                }
            }

            // Orphans: sessions with missing parents still show as top-level.
            foreach (var orphan in visibleSessions.Where(s =>
                         !string.IsNullOrWhiteSpace(effectiveParentBySessionId.GetValueOrDefault(s.Id))
                         && visibleSessions.All(p => !string.Equals(p.Id, effectiveParentBySessionId.GetValueOrDefault(s.Id), StringComparison.Ordinal))))
            {
                var orphanItem = SessionItems.Add(orphan);
                orphanItem.IndentLevel = 0;
                orphanItem.OverrideName = null;
            }

            if (activeSession is not null)
            {
                SelectedSessionItem = SessionItems.FirstOrDefault(i => string.Equals(i.Session.Id, activeSession.Id, StringComparison.Ordinal));
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static string ResolveSessionDisplayName(Session? session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(session.DisplayTitle))
        {
            return session.Name;
        }

        if (string.IsNullOrWhiteSpace(session.Name)
            || string.Equals(session.Name, session.Endpoint, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(session.CapabilityId)
                && session.Name.StartsWith($"{session.CapabilityId} #", StringComparison.Ordinal)))
        {
            return session.DisplayTitle!;
        }

        return session.Name;
    }

    private static bool IsDefaultInboundConnectionName(Session session)
        => !string.IsNullOrWhiteSpace(InferEffectiveParentSessionId(session))
           && (string.IsNullOrWhiteSpace(session.Name)
               || string.Equals(session.Name, session.Endpoint, StringComparison.Ordinal));

    private void OnSessionClosed(SessionClosedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var session = Sessions.FirstOrDefault(s => s.Id == e.SessionId);
            if (session is null)
            {
                return;
            }

            session.Status = SessionStatus.Disconnected;

            if (ActiveSession?.Id == e.SessionId)
            {
                IsConnected = false;
                SyncReconnectSurface();
                ActiveSessionChanged?.Invoke(this, ActiveSession);
            }
        });
    }

    private void OnSessionDeleted(SessionDeletedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var removed = Sessions.FirstOrDefault(s => string.Equals(s.Id, e.SessionId, StringComparison.Ordinal));
            if (removed is null)
            {
                return;
            }

            var nextActiveId = ActiveSession is not null && string.Equals(ActiveSession.Id, e.SessionId, StringComparison.Ordinal)
                ? InferEffectiveParentSessionId(removed)
                : ActiveSession?.Id;

            Sessions.Remove(removed);
            RebuildSessionItems();

            ActiveSession = !string.IsNullOrWhiteSpace(nextActiveId)
                ? Sessions.FirstOrDefault(s => string.Equals(s.Id, nextActiveId, StringComparison.Ordinal))
                : Sessions.FirstOrDefault();
        });
    }

    public int GetChildSessionCount(string parentSessionId)
    {
        if (string.IsNullOrWhiteSpace(parentSessionId))
        {
            return 0;
        }

        return Sessions.Count(session => string.Equals(InferEffectiveParentSessionId(session), parentSessionId, StringComparison.Ordinal));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pluginManager.PluginsReloaded -= _pluginsReloadedHandler;
            _sessionCreatedSubscription.Dispose();
            _sessionUpdatedSubscription.Dispose();
            _sessionClosedSubscription.Dispose();
            _sessionDeletedSubscription.Dispose();
            _workloadSessionMembershipChangedSubscription.Dispose();
            _activeWorkloadChangedSubscription.Dispose();
            QuickCreateSelectorViewModel.Dispose();
            ReconnectEditorSelectorViewModel.Dispose();
            SessionItems.Dispose();
        }

        base.Dispose(disposing);
    }
}
