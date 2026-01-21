using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class LeftSidebarViewModel : BaseViewModel
{
    private readonly IEventBus _eventBus;
    private readonly PluginUiStateManager _pluginUiStateManager;
    private readonly BusAdapterSelectorViewModel _busAdapterSelectorViewModel;
    private readonly IItemVmFactory<SessionListItemViewModel, Session> _sessionItemFactory;

    private readonly IDisposable _sessionCreatedSubscription;
    private readonly IDisposable _sessionClosedSubscription;

    private readonly PropertyChangedEventHandler _activeSessionPropertyChangedHandler;

    private Session? _activeSession;
    private bool _isConnected;
    private string? _preferredActiveSessionId;
    private SessionListItemViewModel? _selectedSessionItem;
    private bool _syncingSelection;

    public LeftSidebarViewModel(
        ILocalizationService localization,
        IEventBus eventBus,
        PluginUiStateManager pluginUiStateManager,
        BusAdapterSelectorViewModel busAdapterSelectorViewModel,
        IItemVmFactory<SessionListItemViewModel, Session> sessionItemFactory)
        : base(localization)
    {
        _eventBus = eventBus;
        _pluginUiStateManager = pluginUiStateManager;
        _busAdapterSelectorViewModel = busAdapterSelectorViewModel;
        _sessionItemFactory = sessionItemFactory;

        _sessionCreatedSubscription = _eventBus.Subscribe<SessionCreatedEvent>(OnSessionCreated);
        _sessionClosedSubscription = _eventBus.Subscribe<SessionClosedEvent>(OnSessionClosed);
        SessionItems = new ItemVmCollection<SessionListItemViewModel, Session>(_sessionItemFactory);

        _activeSessionPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(Session.Status) || string.IsNullOrEmpty(args.PropertyName))
            {
                IsConnected = _activeSession?.Status == SessionStatus.Connected;
            }
        };
    }

    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel => _busAdapterSelectorViewModel;

    public ObservableCollection<Session> Sessions { get; } = new();

    // UI-only collection for typed, compiled bindings.
    public ItemVmCollection<SessionListItemViewModel, Session> SessionItems { get; }

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

            IsConnected = _activeSession?.Status == SessionStatus.Connected;
            ActiveSessionChanged?.Invoke(this, _activeSession);
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public event EventHandler<Session?>? ActiveSessionChanged;

    public void SetPreferredActiveSessionId(string? sessionId)
    {
        _preferredActiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
        TryApplyPreferredActiveSession();
    }

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

    private void OnSessionCreated(SessionCreatedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Sessions.FirstOrDefault(s => s.Id == e.Session.Id);
            if (existing is null)
            {
                Sessions.Add(e.Session);
                existing = e.Session;
            }
            else
            {
                ApplySessionUpdate(existing, e.Session);
            }

            // Keep SessionItems' Session reference consistent with Sessions.
            var itemIndex = -1;
            for (var i = 0; i < SessionItems.Count; i++)
            {
                if (SessionItems[i].Session.Id == existing.Id)
                {
                    itemIndex = i;
                    break;
                }
            }

            if (itemIndex < 0)
            {
                SessionItems.Add(existing);
            }
            else if (!ReferenceEquals(SessionItems[itemIndex].Session, existing))
            {
                var wasSelected = ReferenceEquals(SelectedSessionItem, SessionItems[itemIndex]);
                var replacement = SessionItems.ReplaceAt(itemIndex, existing);
                if (wasSelected)
                {
                    SelectedSessionItem = replacement;
                }
            }

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
            else
            {
                ActiveSession = existing;
            }
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
        target.EnableDatabaseStorage = source.EnableDatabaseStorage;
        target.Status = source.Status;
        target.StartTime = source.StartTime;
    }

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
                ActiveSessionChanged?.Invoke(this, ActiveSession);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sessionCreatedSubscription.Dispose();
            _sessionClosedSubscription.Dispose();
            SessionItems.Dispose();
        }

        base.Dispose(disposing);
    }
}
