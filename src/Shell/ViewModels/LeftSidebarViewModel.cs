using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

public sealed class LeftSidebarViewModel : BaseViewModel
{
    private readonly IEventBus _eventBus;
    private readonly PluginUiStateManager _pluginUiStateManager;
    private readonly BusAdapterSelectorViewModel _busAdapterSelectorViewModel;
    private readonly IObjectFactory _objectFactory;

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
        IObjectFactory objectFactory)
        : base(localization)
    {
        _eventBus = eventBus;
        _pluginUiStateManager = pluginUiStateManager;
        _busAdapterSelectorViewModel = busAdapterSelectorViewModel;
        _objectFactory = objectFactory;

        _eventBus.Subscribe<SessionCreatedEvent>(OnSessionCreated);
        _eventBus.Subscribe<SessionClosedEvent>(OnSessionClosed);
    }

    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel => _busAdapterSelectorViewModel;

    public ObservableCollection<Session> Sessions { get; } = new();

    // UI-only collection for typed, compiled bindings.
    public ObservableCollection<SessionListItemViewModel> SessionItems { get; } = new();

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
            if (!SetProperty(ref _activeSession, value))
            {
                return;
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
            _pluginUiStateManager.SwitchContext(_activeSession?.Id);
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
            return;
        }

        ActiveSession = preferred;
        _preferredActiveSessionId = null;
    }

    private void OnSessionCreated(SessionCreatedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!Sessions.Any(s => s.Id == e.Session.Id))
            {
                Sessions.Add(e.Session);
            }

            if (!SessionItems.Any(i => i.Session.Id == e.Session.Id))
            {
                SessionItems.Add(_objectFactory.Create<SessionListItemViewModel>(e.Session));
            }

            // Prefer restoring last active session (if provided); otherwise activate the newly created one.
            if (!string.IsNullOrWhiteSpace(_preferredActiveSessionId))
            {
                TryApplyPreferredActiveSession();
            }
            else
            {
                ActiveSession = e.Session;
            }
        });
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
}
