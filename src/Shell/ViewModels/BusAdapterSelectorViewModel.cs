using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shell.Models;
using ComCross.Shared.Services;
using ComCross.PluginSdk;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Plugins.UI;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for bus adapter selection and configuration
/// </summary>
public sealed class BusAdapterSelectorViewModel : BaseViewModel
{
    public const string BusAdapterViewKind = "bus-adapter";

    private const string PluginAdapterPrefix = "plugin:";
    private readonly string _viewKind;
    private readonly string? _viewInstanceId;
    private BusAdapterInfo? _selectedAdapter;
    private BusAdapterListItemViewModel? _selectedAdapterItem;
    private Control? _configPanel; // Changed from UserControl to Control
    private string? _activeSessionId;
    private Session? _activeSession;
    private readonly PluginUiRenderer _uiRenderer;
    private readonly PluginUiStateManager _stateManager;
    private readonly PluginManagerViewModel _pluginManager;
    private readonly PluginUiConfigService _pluginUiConfigService;
    private readonly BusAdapterConnectionService _connections;
    private readonly IEventBus _eventBus;
    private readonly IItemVmFactory<BusAdapterListItemViewModel, BusAdapterInfo> _adapterItemFactory;
    private readonly ILogger<BusAdapterSelectorViewModel>? _logger;
    private IReadOnlyList<PluginCapabilityLaunchOption> _lastOptions = Array.Empty<PluginCapabilityLaunchOption>();
    private bool _isSyncingSelection;
    private bool _suppressConfigPanelLoad;
    private int _panelLoadGeneration;
    private readonly EventHandler<string> _languageChangedHandler;
    private readonly PropertyChangedEventHandler _activeSessionPropertyChangedHandler;
    private readonly IDisposable _sessionCreatedSubscription;
    private readonly IDisposable _sessionDeletedSubscription;
    private readonly IDisposable _pluginUiInvalidatedSubscription;
    private int _managedSessionCount;
    private string? _parentSessionDisplayName;
    private ConnectionEditorContext _editorContext = ConnectionEditorContext.Create();
    private PluginResourceActionDescriptor? _rejectAllPendingAction;

    public BusAdapterSelectorViewModel(
        ILocalizationService localization,
        PluginUiRenderer uiRenderer,
        PluginUiStateManager stateManager,
        PluginManagerViewModel pluginManager,
        PluginUiConfigService pluginUiConfigService,
        BusAdapterConnectionService connections,
        IEventBus eventBus,
        IItemVmFactory<BusAdapterListItemViewModel, BusAdapterInfo> adapterItemFactory,
        string viewKind = BusAdapterViewKind,
        string? viewInstanceId = null,
        ILogger<BusAdapterSelectorViewModel>? logger = null)
        : base(localization)
    {
        _viewKind = string.IsNullOrWhiteSpace(viewKind) ? BusAdapterViewKind : viewKind;
        _viewInstanceId = string.IsNullOrWhiteSpace(viewInstanceId) ? null : viewInstanceId;
        _uiRenderer = uiRenderer;
        _stateManager = stateManager;
        _pluginManager = pluginManager;
        _pluginUiConfigService = pluginUiConfigService;
        _connections = connections;
        _eventBus = eventBus;
        _adapterItemFactory = adapterItemFactory;
        _logger = logger;

        AdapterItems = new ItemVmCollection<BusAdapterListItemViewModel, BusAdapterInfo>(_adapterItemFactory);
        ManagedSessions = new ObservableCollection<ManagedSessionRow>();
        PendingResources = new ObservableCollection<PendingResourceRow>();

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect);
        DeleteActiveSessionCommand = new AsyncRelayCommand(DeleteActiveSessionAsync, () => CanDeleteActiveSession);
        AcceptAllPendingCommand = new AsyncRelayCommand(AcceptAllPendingAsync, () => CanAcceptAllPending);
        DisconnectAllManagedSessionsCommand = new AsyncRelayCommand(DisconnectAllManagedSessionsAsync, () => CanDisconnectAllManagedSessions);
        RejectAllPendingCommand = new AsyncRelayCommand(RejectAllPendingAsync, () => CanRejectAllPending);
        DisconnectManagedSessionCommand = new AsyncRelayCommand<string>(DisconnectManagedSessionAsync);
        DeleteManagedSessionCommand = new AsyncRelayCommand<string>(DeleteManagedSessionAsync);

        _activeSessionPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(Session.Status)
                || args.PropertyName == nameof(Session.InitializationState)
                || string.IsNullOrEmpty(args.PropertyName))
            {
                RefreshConnectionCommandState();
            }
        };
        
        _languageChangedHandler = (_, _) =>
        {
            if (_lastOptions.Count > 0)
            {
                UpdatePluginAdapters(_lastOptions);
            }

            // Refresh current config panel so plugin UI labels/layout switch with language.
            if (_selectedAdapter?.PluginId != null && _selectedAdapter.CapabilityId != null)
            {
                _uiRenderer.ClearCache(_selectedAdapter.PluginId, _selectedAdapter.CapabilityId, _editorContext.StateSessionId, _viewKind, _viewInstanceId);
                _ = LoadConfigPanelAsync();
            }
        };

        Localization.LanguageChanged += _languageChangedHandler;
        _sessionCreatedSubscription = _eventBus.Subscribe<SessionCreatedEvent>(OnSessionCreated);
        _sessionDeletedSubscription = _eventBus.Subscribe<SessionDeletedEvent>(OnSessionDeleted);
        _pluginUiInvalidatedSubscription = _eventBus.Subscribe<PluginUiStateInvalidatedCoreEvent>(OnPluginUiStateInvalidated);
    }

    public ItemVmCollection<BusAdapterListItemViewModel, BusAdapterInfo> AdapterItems { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand DeleteActiveSessionCommand { get; }

    public AsyncRelayCommand AcceptAllPendingCommand { get; }

    public AsyncRelayCommand DisconnectAllManagedSessionsCommand { get; }

    public AsyncRelayCommand RejectAllPendingCommand { get; }

    public AsyncRelayCommand<string> DisconnectManagedSessionCommand { get; }

    public AsyncRelayCommand<string> DeleteManagedSessionCommand { get; }

    public ObservableCollection<ManagedSessionRow> ManagedSessions { get; }

    public ObservableCollection<PendingResourceRow> PendingResources { get; }

    public bool IsConnected => _activeSession?.Status == SessionStatus.Connected;

    public bool IsSessionContextMode
        => _editorContext.Mode == ConnectionEditorMode.SessionContext && _activeSession is not null;

    public bool IsCreateMode => _editorContext.Mode is ConnectionEditorMode.Create or ConnectionEditorMode.Reconnect;

    public bool IsManagedResourceMode
        => IsSessionContextMode && _activeSession?.HasManagedResourceKind(PluginResourceKinds.Pending) == true;

    public bool IsSessionDetailMode
        => IsSessionContextMode && _activeSession is not null && !IsManagedResourceMode;

    public bool CanEditConfiguration => !IsConnected && IsCreateMode;

    public bool CanConnect
        => SelectedAdapter?.PluginId != null
           && SelectedAdapter?.CapabilityId != null
           && (SelectedAdapterItem?.IsEnabled ?? false)
           && !IsConnected
           && IsCreateMode;

    public bool CanDisconnect
        => _activeSession?.Id is { Length: > 0 }
           && _activeSession.InitializationState == SessionInitializationState.Ready
           && IsConnected;

    public bool CanDeleteActiveSession
        => _activeSession?.Id is { Length: > 0 }
           && _activeSession.InitializationState is SessionInitializationState.Ready
               or SessionInitializationState.Failed
               or SessionInitializationState.PluginUnavailable;

    public string SessionContextTitle
    {
        get
        {
            if (_activeSession is null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(_activeSession.DisplayTitle)
                ? _activeSession.DisplayTitle
                : _activeSession.Name;
        }
    }

    public string SessionContextEndpoint => _activeSession?.Endpoint ?? string.Empty;

    public string SessionContextStatus
        => _activeSession?.Status == SessionStatus.Connected
            ? L["status.connected"]
            : L["status.disconnected"];

    public string? ParentSessionDisplayName
    {
        get => _parentSessionDisplayName;
        private set => SetProperty(ref _parentSessionDisplayName, value);
    }

    public int ManagedSessionCount
    {
        get => _managedSessionCount;
        private set => SetProperty(ref _managedSessionCount, value);
    }

    public string ManagedSessionSummary
        => string.Format(L["network.session.listener.connections"], ManagedSessionCount);

    public bool HasManagedSessions => ManagedSessions.Count > 0;

    public bool HasPendingResources => PendingResources.Count > 0;

    public bool CanAcceptAllPending => IsManagedResourceMode && HasPendingResources;

    public bool CanDisconnectAllManagedSessions => IsManagedResourceMode && HasManagedSessions;

    public bool CanRejectAllPending => IsManagedResourceMode && HasPendingResources && _rejectAllPendingAction is not null;

    public int PendingResourceCount => PendingResources.Count;

    public string PendingResourceSummary
        => string.Format(L["network.session.pending.connections"], PendingResourceCount);

    public string PendingBulkActionLabel
        => L["network.session.manager.acceptAllPending"];

    public string PendingRejectAllLabel => L["network.session.manager.clearAllPending"];

    public void UpdatePluginAdapters(IReadOnlyList<PluginCapabilityLaunchOption> options)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdatePluginAdapters(options));
            return;
        }

        _lastOptions = options;

        var previouslySelectedId = SelectedAdapter?.Id;

        AdapterItems.Clear();

        if (options.Count > 0)
        {
            foreach (var option in options)
            {
                var pluginName = TryGetLocalized($"{option.PluginId}.name") ?? option.PluginName;
                var capName = TryGetLocalized($"{option.PluginId}.capability.{option.CapabilityId}.name") ?? option.CapabilityName;
                var capDesc = TryGetLocalized($"{option.PluginId}.capability.{option.CapabilityId}.description") ?? option.CapabilityDescription;

                var adapter = new BusAdapterInfo
                {
                    Id = $"{PluginAdapterPrefix}{option.PluginId}:{option.CapabilityId}",
                    Name = Localization.GetString("busAdapter.adapter.nameFormat", pluginName, capName),
                    Icon = string.IsNullOrWhiteSpace(option.Icon) ? "PluginIcon" : option.Icon,
                    Description = capDesc,
                    IsEnabled = true,
                    ConfigPanelType = null,
                    PluginId = option.PluginId,
                    CapabilityId = option.CapabilityId,
                    DefaultParametersJson = option.DefaultParametersJson,
                    ConnectionResource = option.ConnectionResource,
                    JsonSchema = option.JsonSchema,
                    UiSchema = option.UiSchema
                };

                AdapterItems.Add(adapter);
            }
        }

        // Restore selection if possible
        if (!string.IsNullOrWhiteSpace(previouslySelectedId))
        {
            var match = AdapterItems.FirstOrDefault(a => string.Equals(a.Adapter.Id, previouslySelectedId, StringComparison.Ordinal));
            if (match is not null)
            {
                SelectedAdapterItem = match;
                return;
            }
        }

        if (AdapterItems.Count > 0)
        {
            SelectedAdapterItem = AdapterItems[0];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Localization.LanguageChanged -= _languageChangedHandler;
            _sessionCreatedSubscription.Dispose();
            _sessionDeletedSubscription.Dispose();
            _pluginUiInvalidatedSubscription.Dispose();
            AdapterItems.Dispose();
        }

        base.Dispose(disposing);
    }

    public void SelectAdapterById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        var adapter = AdapterItems.FirstOrDefault(a => string.Equals(a.Adapter.Id, id, StringComparison.Ordinal));
        if (adapter is not null)
        {
            SelectedAdapterItem = adapter;
        }
    }

    public void PrepareReconnect(Session? session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => PrepareReconnect(session));
            return;
        }

        SetEditorContext(session is null
            ? ConnectionEditorContext.Create()
            : ConnectionEditorContext.Reconnect(session));
        _ = LoadConfigPanelAsync();
    }

    public Task ExecuteConnectAsync() => ConnectAsync();

    /// <summary>
    /// Currently selected bus adapter
    /// </summary>
    public BusAdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (_selectedAdapter != value)
            {
                _selectedAdapter = value;
                OnPropertyChanged();

                if (!_isSyncingSelection)
                {
                    _isSyncingSelection = true;
                    try
                    {
                        SelectedAdapterItem = value is null
                            ? null
                            : AdapterItems.FirstOrDefault(a => string.Equals(a.Adapter.Id, value.Id, StringComparison.Ordinal));
                    }
                    finally
                    {
                        _isSyncingSelection = false;
                    }
                }

                if (!_suppressConfigPanelLoad)
                {
                    _ = LoadConfigPanelAsync();
                }
            }
        }
    }

    public BusAdapterListItemViewModel? SelectedAdapterItem
    {
        get => _selectedAdapterItem;
        set
        {
            if (_selectedAdapterItem != value)
            {
                _selectedAdapterItem = value;
                OnPropertyChanged();

                if (!_isSyncingSelection)
                {
                    _isSyncingSelection = true;
                    try
                    {
                        SelectedAdapter = value?.Adapter;
                    }
                    finally
                    {
                        _isSyncingSelection = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Configuration panel for the selected adapter
    /// </summary>
    public Control? ConfigPanel
    {
        get => _configPanel;
        private set
        {
            if (_configPanel != value)
            {
                _configPanel = value;
                OnPropertyChanged();
            }
        }
    }

    public void SetActiveSession(Session? session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetActiveSession(session));
            return;
        }

        SetEditorContext(session is null
            ? ConnectionEditorContext.Create()
            : ConnectionEditorContext.SessionContext(session));
    }

    private void SetEditorContext(ConnectionEditorContext context)
    {
        var previousSessionId = _activeSessionId;

        if (_activeSession != null)
        {
            _activeSession.PropertyChanged -= _activeSessionPropertyChangedHandler;
        }

        _editorContext = context;
        var session = context.Session;
        _activeSession = session;
        _activeSessionId = context.SessionId;

        if (_activeSession != null)
        {
            _activeSession.PropertyChanged += _activeSessionPropertyChangedHandler;
        }

        RefreshConnectionCommandState();
        OnPropertyChanged(nameof(IsSessionContextMode));
        OnPropertyChanged(nameof(IsCreateMode));
        OnPropertyChanged(nameof(IsManagedResourceMode));
        OnPropertyChanged(nameof(IsSessionDetailMode));
        OnPropertyChanged(nameof(SessionContextTitle));
        OnPropertyChanged(nameof(SessionContextEndpoint));
        OnPropertyChanged(nameof(SessionContextStatus));
        OnPropertyChanged(nameof(ParentSessionDisplayName));
        OnPropertyChanged(nameof(ManagedSessionSummary));
        OnPropertyChanged(nameof(HasManagedSessions));
        OnPropertyChanged(nameof(HasPendingResources));
        OnPropertyChanged(nameof(PendingResourceSummary));
        OnPropertyChanged(nameof(PendingBulkActionLabel));
        OnPropertyChanged(nameof(PendingRejectAllLabel));
        
        if (IsSessionContextMode)
        {
            ConfigPanel = null;
            _ = RefreshSessionContextAsync();
        }
        else if (session != null && !string.IsNullOrEmpty(session.AdapterId))
        {
            PrepareSessionEditorState(session);
        }

        // Always reload panel when session changes so controls are registered under the correct session.
        // Otherwise, switching sessions can appear to "stop updating" because controls remain bound
        // to the previous session's registration bucket.
        if (!string.Equals(previousSessionId, _activeSessionId, StringComparison.Ordinal) && IsCreateMode)
        {
            _ = LoadConfigPanelAsync();
        }

        // Apply cached per-session state to any already-rendered controls.
        // (Controls registered after UpdateStates won't receive values until SwitchContext is called.)
        {
            var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
            _stateManager.SwitchContext(viewScope, _editorContext.StateSessionId);
        }
    }

    private void PrepareSessionEditorState(Session session)
    {
        _suppressConfigPanelLoad = true;
        try
        {
            var adapterId = ResolveAdapterId(session);
            SelectAdapterById(adapterId);

            if (adapterId is null || !adapterId.StartsWith(PluginAdapterPrefix, StringComparison.Ordinal))
            {
                return;
            }

            ApplyCommittedSessionState(session);
        }
        finally
        {
            _suppressConfigPanelLoad = false;
        }
    }

    private static string? ResolveAdapterId(Session session)
    {
        if (!string.IsNullOrWhiteSpace(session.PluginId)
            && !string.IsNullOrWhiteSpace(session.CapabilityId))
        {
            return $"{PluginAdapterPrefix}{session.PluginId}:{session.CapabilityId}";
        }

        return session.AdapterId;
    }

    private void ApplyCommittedSessionState(Session session)
    {
        var adapterId = ResolveAdapterId(session);
        if (adapterId is null || !adapterId.StartsWith(PluginAdapterPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
        var committed = TryDeserializeObject(session.ParametersJson ?? string.Empty) ?? new Dictionary<string, object>();
        _stateManager.SetStateSnapshot(viewScope, session.Id, committed);
    }

    private void RefreshConnectionCommandState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshConnectionCommandState);
            return;
        }

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanDeleteActiveSession));
        OnPropertyChanged(nameof(CanAcceptAllPending));
        OnPropertyChanged(nameof(CanDisconnectAllManagedSessions));
        OnPropertyChanged(nameof(CanRejectAllPending));
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        DeleteActiveSessionCommand.RaiseCanExecuteChanged();
        AcceptAllPendingCommand.RaiseCanExecuteChanged();
        DisconnectAllManagedSessionsCommand.RaiseCanExecuteChanged();
        RejectAllPendingCommand.RaiseCanExecuteChanged();
    }

    private async Task RefreshSessionContextAsync()
    {
        if (!IsSessionContextMode || _activeSession is null)
        {
            ParentSessionDisplayName = null;
            ManagedSessionCount = 0;
            _rejectAllPendingAction = null;
            ManagedSessions.Clear();
            PendingResources.Clear();
            OnPropertyChanged(nameof(HasManagedSessions));
            OnPropertyChanged(nameof(HasPendingResources));
            OnPropertyChanged(nameof(PendingResourceSummary));
            OnPropertyChanged(nameof(CanAcceptAllPending));
            OnPropertyChanged(nameof(CanDisconnectAllManagedSessions));
            OnPropertyChanged(nameof(CanRejectAllPending));
            AcceptAllPendingCommand.RaiseCanExecuteChanged();
            DisconnectAllManagedSessionsCommand.RaiseCanExecuteChanged();
            RejectAllPendingCommand.RaiseCanExecuteChanged();
            return;
        }

        try
        {
            var sessions = (await _connections.GetActiveSessionsAsync()).ToList();

            if (IsManagedResourceMode)
            {
                var childSessions = sessions
                    .Where(session => string.Equals(session.ParentSessionId, _activeSession.Id, StringComparison.Ordinal))
                    .OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ManagedSessionCount = childSessions.Count;
                ParentSessionDisplayName = null;
                ReplaceManagedSessions(childSessions);
                await RefreshPendingResourceRowsAsync();
                OnPropertyChanged(nameof(ManagedSessionSummary));
                OnPropertyChanged(nameof(HasManagedSessions));
                OnPropertyChanged(nameof(HasPendingResources));
                OnPropertyChanged(nameof(PendingResourceSummary));
                OnPropertyChanged(nameof(CanAcceptAllPending));
                OnPropertyChanged(nameof(CanDisconnectAllManagedSessions));
                OnPropertyChanged(nameof(CanRejectAllPending));
                AcceptAllPendingCommand.RaiseCanExecuteChanged();
                DisconnectAllManagedSessionsCommand.RaiseCanExecuteChanged();
                RejectAllPendingCommand.RaiseCanExecuteChanged();
                return;
            }

            ManagedSessionCount = 0;
            _rejectAllPendingAction = null;
            ManagedSessions.Clear();
            PendingResources.Clear();
            if (!string.IsNullOrWhiteSpace(_activeSession.ParentSessionId))
            {
                ParentSessionDisplayName = sessions
                    .FirstOrDefault(session => string.Equals(session.Id, _activeSession.ParentSessionId, StringComparison.Ordinal))
                    ?.Name;
            }
            else
            {
                ParentSessionDisplayName = null;
            }
        }
        catch
        {
            ManagedSessionCount = 0;
            _rejectAllPendingAction = null;
            ParentSessionDisplayName = null;
            ManagedSessions.Clear();
            PendingResources.Clear();
        }

        OnPropertyChanged(nameof(HasManagedSessions));
        OnPropertyChanged(nameof(HasPendingResources));
        OnPropertyChanged(nameof(PendingResourceSummary));
        OnPropertyChanged(nameof(CanAcceptAllPending));
        OnPropertyChanged(nameof(CanDisconnectAllManagedSessions));
        OnPropertyChanged(nameof(CanRejectAllPending));
        AcceptAllPendingCommand.RaiseCanExecuteChanged();
        DisconnectAllManagedSessionsCommand.RaiseCanExecuteChanged();
        RejectAllPendingCommand.RaiseCanExecuteChanged();
    }

    private void ReplaceManagedSessions(IReadOnlyList<Session> sessions)
    {
        ManagedSessions.Clear();
        foreach (var session in sessions)
        {
            ManagedSessions.Add(new ManagedSessionRow(
                session.Id,
                session.Name,
                session.Endpoint,
                session.Status,
                session.RxBytes,
                session.TxBytes,
                L["status.tx"],
                L["status.rx"],
                L["menu.disconnect"],
                L["session.menu.delete"],
                L["status.connected"],
                L["status.disconnected"],
                DisconnectManagedSessionAsync,
                DeleteManagedSessionAsync));
        }
    }

    private async Task RefreshPendingResourceRowsAsync()
    {
        PendingResources.Clear();
        _rejectAllPendingAction = null;

        if (!IsManagedResourceMode
            || _activeSession is null
            || string.IsNullOrWhiteSpace(_activeSession.PluginId)
            || string.IsNullOrWhiteSpace(_activeSession.CapabilityId))
        {
            return;
        }

        var state = await _pluginManager.TryGetUiStateAsync(
            _activeSession.PluginId,
            _activeSession.CapabilityId,
            _activeSession.Id,
            viewKind: "managed-resource",
            viewInstanceId: null,
            resourceKind: PluginResourceKinds.Pending,
            resourceId: PluginResourceIds.All,
            timeout: TimeSpan.FromSeconds(1));

        if (state is null || state.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        PluginResourceListState? resourceState;
        try
        {
            resourceState = System.Text.Json.JsonSerializer.Deserialize<PluginResourceListState>(
                state.Value.GetRawText(),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return;
        }

        if (resourceState is null
            || resourceState.Items is null
            || !string.Equals(resourceState.ResourceKind, PluginResourceKinds.Pending, StringComparison.Ordinal))
        {
            return;
        }

        _rejectAllPendingAction = FindResourceAction(resourceState.BulkActions, PluginResourceActionIds.RejectAll);

        foreach (var item in resourceState.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            var acceptAction = FindResourceAction(item.Actions, PluginResourceActionIds.Accept);
            var rejectAction = FindResourceAction(item.Actions, PluginResourceActionIds.Reject);
            PendingResources.Add(new PendingResourceRow(
                item.Id,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName,
                ResolveResourceActionLabel(acceptAction, GetPendingActionLabel()),
                ResolveResourceActionLabel(rejectAction, L["network.session.manager.rejectPending"]),
                acceptAction,
                rejectAction,
                AcceptPendingResourceAsync,
                RejectPendingResourceAsync));
        }

        OnPropertyChanged(nameof(HasPendingResources));
        OnPropertyChanged(nameof(PendingResourceSummary));
    }

    private string ResolveResourceActionLabel(PluginResourceActionDescriptor? action, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(action?.LabelKey))
        {
            return L[action.LabelKey];
        }

        return string.IsNullOrWhiteSpace(action?.Label) ? fallback : action.Label;
    }

    private static PluginResourceActionDescriptor? FindResourceAction(
        IReadOnlyList<PluginResourceActionDescriptor>? actions,
        string actionId)
        => actions?.FirstOrDefault(action => string.Equals(action.Id, actionId, StringComparison.Ordinal));

    private string GetPendingActionLabel()
        => L["network.session.manager.acceptPending"];

    private async Task AcceptAllPendingAsync()
    {
        if (!CanAcceptAllPending)
        {
            return;
        }

        var pendingSnapshot = PendingResources.ToList();
        foreach (var item in pendingSnapshot)
        {
            await AcceptPendingResourceCoreAsync(item.PendingId, item.DisplayName, item.ConnectAction, refreshAfter: false);
        }

        await RefreshSessionContextAsync();
    }

    private async Task RejectAllPendingAsync()
    {
        if (!CanRejectAllPending || _activeSession is null)
        {
            return;
        }

        var confirmed = await MessageBoxService.ShowConfirmAsync(
            L["dialog.network.clearPending.title"],
            string.Format(L["dialog.network.clearPending.message"], _activeSession.Name));
        if (!confirmed)
        {
            return;
        }

        if (_rejectAllPendingAction is null)
        {
            return;
        }

        var (ok, error) = await ExecuteResourceActionAsync(_activeSession, _rejectAllPendingAction);

        if (!ok)
        {
            await MessageBoxService.ShowErrorAsync(
                L["dialog.deleteSession.title"],
                error ?? L["error.unknown"]);
            return;
        }

        await RefreshSessionContextAsync();
    }

    private async Task AcceptPendingResourceAsync(
        string? pendingId,
        string? displayName,
        PluginResourceActionDescriptor? action)
        => await AcceptPendingResourceCoreAsync(pendingId, displayName, action, refreshAfter: true);

    private async Task RejectPendingResourceAsync(string? pendingId, PluginResourceActionDescriptor? action)
    {
        if (!IsManagedResourceMode || _activeSession is null || string.IsNullOrWhiteSpace(pendingId) || action is null)
        {
            return;
        }

        var (ok, error) = await ExecuteResourceActionAsync(_activeSession, action);

        if (!ok)
        {
            await MessageBoxService.ShowErrorAsync(
                L["dialog.deleteSession.title"],
                error ?? L["error.unknown"]);
            return;
        }

        await RefreshSessionContextAsync();
    }

    private async Task AcceptPendingResourceCoreAsync(
        string? pendingId,
        string? displayName,
        PluginResourceActionDescriptor? action,
        bool refreshAfter)
    {
        if (!IsManagedResourceMode
            || _activeSession is null
            || action is null
            || string.IsNullOrWhiteSpace(pendingId)
            || string.IsNullOrWhiteSpace(_activeSession.PluginId)
            || string.IsNullOrWhiteSpace(_activeSession.CapabilityId)
            || !string.Equals(action.Kind, PluginResourceActionKinds.ConnectScopedResource, StringComparison.Ordinal))
        {
            return;
        }

        var parametersJson = action.Parameters is { } parameters
            ? parameters.GetRawText()
            : "{}";
        var sessionName = string.IsNullOrWhiteSpace(action.SessionName)
            ? string.IsNullOrWhiteSpace(displayName) ? null : displayName
            : action.SessionName;

        await _connections.ConnectScopedResourceAsync(
            _activeSession.PluginId,
            _activeSession.CapabilityId,
            parametersJson,
            sessionName,
            _activeSession.Id,
            PluginResourceKinds.Pending,
            pendingId);

        if (refreshAfter)
        {
            await RefreshSessionContextAsync();
        }
    }

    private async Task<(bool Ok, string? Error)> ExecuteResourceActionAsync(
        Session session,
        PluginResourceActionDescriptor action)
    {
        if (!string.Equals(action.Kind, PluginResourceActionKinds.ExecuteAction, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(session.PluginId)
            || string.IsNullOrWhiteSpace(action.ActionName))
        {
            return (false, L["error.unknown"]);
        }

        object parameters = action.Parameters is { } value ? value : new { };
        return await _pluginManager.ExecutePluginActionAsync(
            session.PluginId,
            session.Id,
            action.ActionName,
            parameters);
    }

    private static bool IsNullOrBlank(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is string s)
        {
            return string.IsNullOrWhiteSpace(s);
        }

        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Undefined => true,
                System.Text.Json.JsonValueKind.Null => true,
                System.Text.Json.JsonValueKind.String => string.IsNullOrWhiteSpace(je.GetString()),
                _ => false
            };
        }

        return false;
    }

    private async System.Threading.Tasks.Task ConnectAsync()
    {
        if (SelectedAdapter?.PluginId is not { Length: > 0 } pluginId
            || SelectedAdapter.CapabilityId is not { Length: > 0 } capabilityId)
        {
            return;
        }

        var stateKey = _editorContext.StateSessionId;
        var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
        var currentState = _stateManager.GetState(viewScope, stateKey);

        // Minimal required-field validation (Plugin UI schema v1 uses field.Required).
        var uiSchema = PluginUiSchema.TryParse(SelectedAdapter.UiSchema);
        if (uiSchema?.Fields is { Count: > 0 })
        {
            foreach (var field in uiSchema.Fields)
            {
                if (!field.Required)
                {
                    continue;
                }

                if (!currentState.TryGetValue(field.Key, out var value) || value is null)
                {
                    await MessageBoxService.ShowWarningAsync(
                        Localization.GetString("dialog.connect.title"),
                        string.Format(Localization.GetString("dialog.connect.validation.requiredField"), field.Label ?? field.Key));
                    return;
                }

                if (value is string s && string.IsNullOrWhiteSpace(s))
                {
                    await MessageBoxService.ShowWarningAsync(
                        Localization.GetString("dialog.connect.title"),
                        string.Format(Localization.GetString("dialog.connect.validation.requiredField"), field.Label ?? field.Key));
                    return;
                }
            }
        }

        // Only explicit reconnect reuses a session id. Plain connect always creates a new session.
        var targetSessionId = _editorContext.ReconnectSessionId;
        if (targetSessionId is not null && _activeSession?.CanReconnect != true)
        {
            return;
        }

        var connectionResource = SelectedAdapter.ConnectionResource;
        if (targetSessionId is null
            && connectionResource?.PromptDisconnectExisting == true
            && !string.IsNullOrWhiteSpace(connectionResource.ParameterKey))
        {
            var desiredResource = TryGetStateString(currentState, connectionResource.ParameterKey);
            if (!string.IsNullOrWhiteSpace(desiredResource))
            {
                try
                {
                    var conflict = await _connections.FindResourceConflictAsync(
                        pluginId,
                        capabilityId,
                        connectionResource.ParameterKey,
                        desiredResource);

                    if (conflict is not null)
                    {
                        var resourceLabel = string.IsNullOrWhiteSpace(connectionResource.LabelKey)
                            ? connectionResource.ParameterKey
                            : Localization.GetString(connectionResource.LabelKey);
                        var ok = await MessageBoxService.ShowConfirmAsync(
                            Localization.GetString("dialog.connect.title"),
                            Localization.GetString(
                                "dialog.connect.resourceConflict.disconnectExisting",
                                resourceLabel,
                                desiredResource,
                                conflict.Name));

                        if (!ok)
                        {
                            return;
                        }

                        await _connections.DisconnectPluginAsync(pluginId, conflict.Id);
                    }
                }
                catch
                {
                    // Best-effort; if we can't query sessions, just attempt connect.
                }
            }
        }

        try
        {
            await _connections.ConnectPluginAsync(pluginId, capabilityId, targetSessionId, currentState);
            await RefreshActiveSessionFromWorkspaceAsync(targetSessionId);
            RefreshConnectionCommandState();
        }
        catch (Exception ex)
        {
            // If the underlying plugin host still enforces single-session, surface a clearer hint.
            // i18n-ignore (non-UI: matching a backend error message)
            if (ex.Message.Contains("Another session is already active", StringComparison.OrdinalIgnoreCase))
            {
                await MessageBoxService.ShowErrorAsync(
                    Localization.GetString("dialog.connect.title"),
                    Localization.GetString("dialog.connect.plugin.singleSessionHint"));
                return;
            }

            await MessageBoxService.ShowErrorAsync(
                Localization.GetString("dialog.connect.title"),
                ex.Message);
            return;
        }
    }

    private static string? TryGetStateString(IDictionary<string, object> state, string key)
    {
        if (!state.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return je.GetString();
            }

            return je.ToString();
        }

        return value.ToString();
    }

    private static Dictionary<string, object>? TryDeserializeObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    private async System.Threading.Tasks.Task DisconnectAsync()
    {
        if (_activeSession?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        if (_activeSession.PluginId is not { Length: > 0 } pluginId)
        {
            pluginId = string.Empty;
        }

        await _connections.DisconnectPluginAsync(pluginId, sessionId);
        await RefreshActiveSessionFromWorkspaceAsync(sessionId);
        RefreshConnectionCommandState();
    }

    private async Task RefreshActiveSessionFromWorkspaceAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var session = await _connections.GetActiveSessionAsync(sessionId);
            if (session is null)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyActiveSessionSnapshot(session);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => ApplyActiveSessionSnapshot(session));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to refresh active session snapshot after lifecycle action: session={SessionId}", sessionId);
        }
    }

    private void ApplyActiveSessionSnapshot(Session session)
    {
        if (!string.Equals(_activeSessionId, session.Id, StringComparison.Ordinal)
            && !string.Equals(_editorContext.ReconnectSessionId, session.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (!ReferenceEquals(_activeSession, session))
        {
            SetEditorContext(_editorContext.Mode == ConnectionEditorMode.Reconnect
                ? ConnectionEditorContext.Reconnect(session)
                : ConnectionEditorContext.SessionContext(session));
            return;
        }

        RefreshConnectionCommandState();
        OnPropertyChanged(nameof(SessionContextStatus));
        OnPropertyChanged(nameof(SessionContextEndpoint));
    }

    private async Task DeleteActiveSessionAsync()
    {
        if (_activeSession?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        var message = IsManagedResourceMode
            ? string.Format(L["dialog.deleteSession.listener.message"], _activeSession.Name)
            : string.Format(L["dialog.deleteSession.message"], _activeSession.Name);

        var confirmed = await MessageBoxService.ShowConfirmAsync(
            L["dialog.deleteSession.title"],
            message);
        if (!confirmed)
        {
            return;
        }

        await _connections.DeleteSessionAsync(sessionId);
    }

    private async Task DisconnectManagedSessionAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _connections.CloseSessionAsync(sessionId);
        await RefreshSessionContextAsync();
    }

    private async Task DisconnectAllManagedSessionsAsync()
    {
        if (!CanDisconnectAllManagedSessions)
        {
            return;
        }

        var sessionIds = ManagedSessions
            .Select(item => item.SessionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        foreach (var sessionId in sessionIds)
        {
            await _connections.CloseSessionAsync(sessionId);
        }

        await RefreshSessionContextAsync();
    }

    private async Task DeleteManagedSessionAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var row = ManagedSessions.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
        var displayName = row?.Name ?? sessionId;
        var confirmed = await MessageBoxService.ShowConfirmAsync(
            L["dialog.deleteSession.title"],
            string.Format(L["dialog.deleteSession.message"], displayName));
        if (!confirmed)
        {
            return;
        }

        await _connections.DeleteSessionAsync(sessionId);
        await RefreshSessionContextAsync();
    }

    /// <summary>
    /// Load the configuration panel for the selected adapter
    /// </summary>
    private async System.Threading.Tasks.Task LoadConfigPanelAsync()
    {
        if (IsSessionContextMode)
        {
            ConfigPanel = null;
            return;
        }

        var generation = System.Threading.Interlocked.Increment(ref _panelLoadGeneration);
        var expectedContext = _editorContext;
        var adapter = _selectedAdapter;
        var stateSessionId = expectedContext.StateSessionId;
        var expectedAdapterId = adapter?.Id;
        var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
        ConfigPanel = null;

        bool IsStale()
            => generation != System.Threading.Volatile.Read(ref _panelLoadGeneration)
               || !Equals(expectedContext, _editorContext)
               || !string.Equals(expectedAdapterId, _selectedAdapter?.Id, StringComparison.Ordinal);

        if (adapter?.ConfigPanelType != null)
        {
            try
            {
                // Create instance of the config panel
                var panel = Activator.CreateInstance(adapter.ConfigPanelType) as Control;
                if (IsStale())
                {
                    return;
                }

                ConfigPanel = panel;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load config panel for {adapter.Name}: {ex.Message}");
                if (IsStale())
                {
                    return;
                }

                ConfigPanel = null;
            }
        }
        else if (adapter?.PluginId != null)
        {
             // Plugin-backed adapter: Use PluginUiRenderer
             var uiSchema = PluginUiSchema.TryParse(adapter.UiSchema);
             var jsonSchema = TryParseJsonSchema(adapter.JsonSchema);
             var fields = uiSchema?.Fields;

             var optionKeys = new HashSet<string>(StringComparer.Ordinal);
             if (fields is { Count: > 0 })
             {
                 foreach (var field in fields)
                 {
                     if (!string.IsNullOrWhiteSpace(field.OptionsStatePath))
                     {
                         optionKeys.Add(field.OptionsStatePath);
                     }
                 }
             }

             if (fields != null && fields.Count > 0 && jsonSchema != null)
             {
                 EnrichFieldsFromJsonSchema(fields, jsonSchema.Value);
             }

             // Pull UI state (ports, defaults, etc.) so select/options populate.
             try
             {
                 if (adapter.CapabilityId != null)
                 {
                     var uiState = await _pluginManager.TryGetUiStateAsync(
                         adapter.PluginId,
                         adapter.CapabilityId,
                         sessionId: stateSessionId,
                         viewKind: _viewKind,
                         viewInstanceId: _viewInstanceId,
                         resourceKind: null,
                         resourceId: null,
                         timeout: TimeSpan.FromSeconds(2));

                     if (uiState is null && !string.IsNullOrWhiteSpace(stateSessionId))
                     {
                         uiState = await _pluginManager.TryGetUiStateAsync(
                             adapter.PluginId,
                             adapter.CapabilityId,
                             sessionId: null,
                             viewKind: _viewKind,
                             viewInstanceId: _viewInstanceId,
                             resourceKind: null,
                             resourceId: null,
                             timeout: TimeSpan.FromSeconds(2));
                     }

                     if (uiState is not null && uiState.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                     {
                         var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(uiState.Value.GetRawText());
                         if (dict != null)
                         {
                             // IMPORTANT: uiState contains dynamic host/UI data (options, defaults, ports, etc.).
                             // It must not overwrite session connection parameters already seeded from Session.ParametersJson.
                             // Merge strategy:
                             // - Always apply option keys (e.g. select options like "ports")
                             // - For all other keys, only fill when the current state is missing/blank
                             var current = _stateManager.GetState(viewScope, stateSessionId);
                             var toApply = new Dictionary<string, object>(StringComparer.Ordinal);

                             foreach (var kvp in dict)
                             {
                                 if (string.IsNullOrWhiteSpace(kvp.Key))
                                 {
                                     continue;
                                 }

                                 var forceUpdate = optionKeys.Contains(kvp.Key)
                                                  || string.Equals(kvp.Key, "ports", StringComparison.Ordinal);

                                 if (forceUpdate)
                                 {
                                     toApply[kvp.Key] = kvp.Value;
                                     continue;
                                 }

                                 if (!current.TryGetValue(kvp.Key, out var existing) || IsNullOrBlank(existing))
                                 {
                                     toApply[kvp.Key] = kvp.Value;
                                 }
                             }

                             if (toApply.Count > 0)
                             {
                                 _stateManager.UpdateStates(viewScope, stateSessionId, toApply);
                             }

                             if (fields is { Count: > 0 })
                             {
                                 // Project defaults into field keys, but only when the field value is missing/blank.
                                 var projected = ProjectDefaults(dict, fields);
                                 if (projected.Count > 0)
                                 {
                                     var after = _stateManager.GetState(viewScope, stateSessionId);
                                     var projectedToApply = new Dictionary<string, object>(StringComparer.Ordinal);
                                     foreach (var kvp in projected)
                                     {
                                         if (!after.TryGetValue(kvp.Key, out var existing) || IsNullOrBlank(existing))
                                         {
                                             projectedToApply[kvp.Key] = kvp.Value;
                                         }
                                     }

                                     if (projectedToApply.Count > 0)
                                     {
                                         _stateManager.UpdateStates(viewScope, stateSessionId, projectedToApply);
                                     }
                                 }
                             }
                         }
                     }
                 }
             }
             catch
             {
                 // best-effort
             }
             
             // If no fields, we don't show a panel (or could fallback to JsonSchema, but keeping it simple for now)
             var pluginId = adapter.PluginId;
             var capabilityId = adapter.CapabilityId;
             if (fields != null && fields.Count > 0 && pluginId != null && capabilityId != null)
             {
                 // Preserve UiSchemaVersion1 layout/title metadata if present.
                 var schema = uiSchema ?? new PluginUiSchema { Fields = fields };
                 schema.Fields = fields;

                 if (stateSessionId is null)
                 {
                     await _pluginUiConfigService.SeedStateAsync(
                         pluginId,
                         capabilityId,
                         schema,
                         sessionId: null,
                         viewKind: _viewKind,
                         viewInstanceId: _viewInstanceId);
                 }

                 _uiRenderer.ClearCache(pluginId, capabilityId, stateSessionId, _viewKind, _viewInstanceId);
                 var container = _uiRenderer.GetOrRender(pluginId, capabilityId, schema, stateSessionId, _viewKind, _viewInstanceId);
                 if (container is AvaloniaPluginUiContainer avaloniaContainer)
                 {
                     if (IsStale())
                     {
                         return;
                     }

                     ConfigPanel = avaloniaContainer.GetPanel();

                     // Ensure newly created/registered controls receive cached state.
                     _stateManager.SwitchContext(viewScope, stateSessionId);
                     if (_activeSession is not null && string.Equals(_activeSession.Id, stateSessionId, StringComparison.Ordinal))
                     {
                         ApplyCommittedSessionState(_activeSession);
                         _stateManager.SwitchContext(viewScope, stateSessionId);
                     }
                 }
                 else
                 {
                     if (IsStale())
                     {
                         return;
                     }

                     ConfigPanel = null;
                 }
             }
             else
             {
                 if (IsStale())
                 {
                     return;
                 }

                 ConfigPanel = null;
             }
        }
        else
        {
            // No custom config panel, use default or null
            if (IsStale())
            {
                return;
            }

            ConfigPanel = null;
        }
    }

    private void OnSessionCreated(SessionCreatedEvent evt)
    {
        if (string.Equals(evt.Session.Id, _activeSessionId, StringComparison.Ordinal)
            || string.Equals(evt.Session.Id, _editorContext.ReconnectSessionId, StringComparison.Ordinal))
        {
            Dispatcher.UIThread.Post(() => ApplyActiveSessionSnapshot(evt.Session));
            return;
        }

        if (!IsSessionContextMode || _activeSession is null)
        {
            return;
        }

        if (IsManagedResourceMode && string.Equals(evt.Session.ParentSessionId, _activeSession.Id, StringComparison.Ordinal))
        {
            Dispatcher.UIThread.Post(() => _ = RefreshSessionContextAsync());
        }
    }

    private void OnSessionDeleted(SessionDeletedEvent evt)
    {
        if (!IsSessionContextMode || _activeSession is null)
        {
            return;
        }

        if (IsManagedResourceMode)
        {
            Dispatcher.UIThread.Post(() => _ = RefreshSessionContextAsync());
        }
        else if (string.Equals(_activeSession.ParentSessionId, evt.SessionId, StringComparison.Ordinal))
        {
            Dispatcher.UIThread.Post(() => _ = RefreshSessionContextAsync());
        }
    }

    private void OnPluginUiStateInvalidated(PluginUiStateInvalidatedCoreEvent evt)
    {
        if (!IsManagedResourceMode || _activeSession is null)
        {
            return;
        }

        if (!string.Equals(evt.PluginId, _activeSession.PluginId, StringComparison.Ordinal)
            || !string.Equals(evt.SessionId, _activeSession.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(evt.ResourceKind, PluginResourceKinds.Pending, StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = RefreshSessionContextAsync());
    }

    private string? TryGetLocalized(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var value = Localization.GetString(key);
        if (string.Equals(value, $"[{key}]", StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    private static System.Text.Json.JsonElement? TryParseJsonSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static void EnrichFieldsFromJsonSchema(List<PluginUiField> fields, System.Text.Json.JsonElement schema)
    {
        // Minimal enum extraction: { properties: { fieldName: { enum: [...] } } }
        if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in fields)
        {
            if (!field.EnumFromSchema || field.GetOptionsAsOptionList().Count > 0)
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(field.Name) ? field.Name : field.Key;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!props.TryGetProperty(name, out var propSchema) || propSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            if (!propSchema.TryGetProperty("enum", out var enums) || enums.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            var list = new List<string>();
            foreach (var item in enums.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }
            }

            if (list.Count > 0)
            {
                field.Options = System.Text.Json.JsonSerializer.SerializeToElement(list);
            }
        }
    }

    private static Dictionary<string, object> ProjectDefaults(Dictionary<string, object> uiState, List<PluginUiField> fields)
    {
        var projected = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || string.IsNullOrWhiteSpace(field.DefaultStatePath))
            {
                continue;
            }

            if (TryGetPath(uiState, field.DefaultStatePath!, out var value) && value is not null)
            {
                projected[field.Key] = value;
            }
        }

        return projected;
    }

    private static bool TryGetPath(Dictionary<string, object> root, string path, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path)) return false;

        object? current = root;
        foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(seg, out current)) return false;
                continue;
            }

            if (current is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (!je.TryGetProperty(seg, out var child)) return false;
                current = child;
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }

}

public sealed class ManagedSessionRow
{
    private readonly string _connectedText;
    private readonly string _disconnectedText;

    public ManagedSessionRow(
        string sessionId,
        string name,
        string endpoint,
        SessionStatus status,
        long rxBytes,
        long txBytes,
        string txLabel,
        string rxLabel,
        string disconnectLabel,
        string deleteLabel,
        string connectedText,
        string disconnectedText,
        Func<string?, Task> disconnectAsync,
        Func<string?, Task> deleteAsync)
    {
        SessionId = sessionId;
        Name = name;
        Endpoint = endpoint;
        Status = status;
        RxBytes = rxBytes;
        TxBytes = txBytes;
        TxLabel = txLabel;
        RxLabel = rxLabel;
        DisconnectLabel = disconnectLabel;
        DeleteLabel = deleteLabel;
        _connectedText = connectedText;
        _disconnectedText = disconnectedText;
        DisconnectCommand = new AsyncRelayCommand(() => disconnectAsync(SessionId));
        DeleteCommand = new AsyncRelayCommand(() => deleteAsync(SessionId));
    }

    public string SessionId { get; }

    public string Name { get; }

    public string Endpoint { get; }

    public SessionStatus Status { get; }

    public long RxBytes { get; }

    public long TxBytes { get; }

    public string TxLabel { get; }

    public string RxLabel { get; }

    public string DisconnectLabel { get; }

    public string DeleteLabel { get; }

    public string StatusText
        => Status == SessionStatus.Connected ? _connectedText : _disconnectedText;

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }
}

public sealed record PendingResourceRow(
    string PendingId,
    string DisplayName,
    string ActionLabel,
    string RejectLabel,
    PluginResourceActionDescriptor? ConnectAction,
    PluginResourceActionDescriptor? RejectAction,
    Func<string?, string?, PluginResourceActionDescriptor?, Task> ConnectAsync,
    Func<string?, PluginResourceActionDescriptor?, Task> RejectAsync)
{
    public AsyncRelayCommand ConnectCommand { get; } = new(() => ConnectAsync(PendingId, DisplayName, ConnectAction));

    public AsyncRelayCommand RejectCommand { get; } = new(() => RejectAsync(PendingId, RejectAction));
}
