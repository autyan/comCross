using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
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

    // Simplified listener-mode UI: allocate stable connection indices for child sessions.
    private readonly Dictionary<string, int> _connectionIndexBySessionId = new(StringComparer.Ordinal);
    private int _nextConnectionIndex;

    // UI-only state: collapsed listener nodes.
    private readonly HashSet<string> _collapsedListenerSessionIds = new(StringComparer.Ordinal);

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

    public void RefreshSessionItems()
    {
        // Sessions can be mutated by other VMs (e.g., delete). Ensure the derived UI list stays in sync.
        Dispatcher.UIThread.Post(RebuildSessionItems);
    }

    public void ToggleListenerCollapsed(string listenerSessionId)
    {
        if (string.IsNullOrWhiteSpace(listenerSessionId))
        {
            return;
        }

        if (_collapsedListenerSessionIds.Contains(listenerSessionId))
        {
            _collapsedListenerSessionIds.Remove(listenerSessionId);
        }
        else
        {
            _collapsedListenerSessionIds.Add(listenerSessionId);
        }

        Dispatcher.UIThread.Post(RebuildSessionItems);
    }

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
        target.Kind = source.Kind;
        target.ParentSessionId = source.ParentSessionId;
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
    {
        if (!string.IsNullOrWhiteSpace(session.ParentSessionId))
        {
            return session.ParentSessionId;
        }

        // Back-compat / robustness: infer listener-child topology from persisted parameters.
        if (!string.Equals(session.PluginId, "network.adapter", StringComparison.Ordinal)
            || session.CapabilityId is not ("tcp.server" or "udp.listen")
            || string.IsNullOrWhiteSpace(session.ParametersJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(session.ParametersJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("mode", out var modeEl))
            {
                return null;
            }

            var mode = modeEl.ValueKind == JsonValueKind.String ? modeEl.GetString() : modeEl.ToString();
            if (!string.Equals(mode, "bind", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!root.TryGetProperty("listenerSessionId", out var parentEl))
            {
                return null;
            }

            var parent = parentEl.ValueKind == JsonValueKind.String ? parentEl.GetString() : parentEl.ToString();
            return string.IsNullOrWhiteSpace(parent) ? null : parent;
        }
        catch
        {
            return null;
        }
    }

    private void RebuildSessionItems()
    {
        // Preserve the current active session while we rebuild viewmodels.
        var activeSession = _activeSession;

        // Keep connection index map bounded to current sessions.
        var aliveIds = Sessions.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _connectionIndexBySessionId.Keys.Where(k => !aliveIds.Contains(k)).ToList())
        {
            _connectionIndexBySessionId.Remove(key);
        }

        var effectiveParentBySessionId = Sessions.ToDictionary(
            s => s.Id,
            InferEffectiveParentSessionId,
            StringComparer.Ordinal);

        _syncingSelection = true;
        try
        {
            SelectedSessionItem = null;
            SessionItems.Clear();

            // Display order: top-level sessions first; children (connections) immediately after their listener.
            foreach (var top in Sessions.Where(s => string.IsNullOrWhiteSpace(effectiveParentBySessionId.GetValueOrDefault(s.Id))))
            {
                var topItem = SessionItems.Add(top);
                topItem.IndentLevel = 0;
                topItem.OverrideName = (string.Equals(top.PluginId, "network.adapter", StringComparison.Ordinal) && top.CapabilityId is "tcp.server")
                    ? "TCP Listener"
                    : (string.Equals(top.PluginId, "network.adapter", StringComparison.Ordinal) && top.CapabilityId is "udp.listen")
                        ? "UDP Listener"
                        : null;

                topItem.IsCollapsed = topItem.IsListener && _collapsedListenerSessionIds.Contains(top.Id);

                if (topItem.IsListener && topItem.IsCollapsed)
                {
                    continue;
                }

                foreach (var child in Sessions
                             .Where(s => string.Equals(effectiveParentBySessionId.GetValueOrDefault(s.Id), top.Id, StringComparison.Ordinal))
                             .OrderBy(s => _connectionIndexBySessionId.TryGetValue(s.Id, out var idx) ? idx : int.MaxValue))
                {
                    EnsureConnectionIndex(child, top.Id);
                    var idx = _connectionIndexBySessionId.TryGetValue(child.Id, out var assigned) ? assigned : 0;

                    var childItem = SessionItems.Add(child);
                    childItem.IndentLevel = 1;
                    childItem.OverrideName = idx > 0 ? $"Conn #{idx}" : "Conn";
                }
            }

            // Orphans: sessions with missing parents still show as top-level.
            foreach (var orphan in Sessions.Where(s =>
                         !string.IsNullOrWhiteSpace(effectiveParentBySessionId.GetValueOrDefault(s.Id))
                         && Sessions.All(p => !string.Equals(p.Id, effectiveParentBySessionId.GetValueOrDefault(s.Id), StringComparison.Ordinal))))
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
