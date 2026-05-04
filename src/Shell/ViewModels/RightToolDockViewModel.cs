using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class RightToolDockViewModel : BaseViewModel
{
    public const int MaxPinnedCommands = 8;

    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly AppLogService _appLogService;
    private readonly MessageStreamViewModel _messageStream;
    private readonly IDisposable _pluginUiStateInvalidatedSubscription;

    private Session? _activeSession;
    private bool _isConnected;
    private bool _requiresTransmitTarget;
    private bool _isSendHexMode;
    private bool _isAdvancedOptionsOpen;
    private bool _clearAfterSend;
    private string _messageInput = string.Empty;
    private string? _sendErrorText;
    private string _commandSearchQuery = string.Empty;
    private CommandGroupFilterOption? _selectedCommandGroupFilter;
    private CommandListItemViewModel? _selectedSearchCommand;
    private bool _isCommandSearchOpen;
    private bool _isSelectingSearchCommand;
    private bool _isCommandEditorOpen;
    private ToolDockTab _selectedToolTab = ToolDockTab.Send;
    private TransmitTargetListItemViewModel? _selectedTransmitTarget;
    private int _transmitTargetRefreshVersion;

    public RightToolDockViewModel(
        ILocalizationService localization,
        IWorkspaceCoordinator workspaceCoordinator,
        IEventBus eventBus,
        AppLogService appLogService,
        MessageStreamViewModel messageStream,
        SettingsViewModel settings,
        CommandCenterViewModel commandCenter)
        : base(localization)
    {
        _workspaceCoordinator = workspaceCoordinator;
        _appLogService = appLogService;
        _messageStream = messageStream;

        Settings = settings;
        CommandCenter = commandCenter;
        CommandCenter.Commands.CollectionChanged += OnCommandsChanged;
        SyncCommandGroups();
        SyncQuickCommands();
        SyncSearchCommands();
        _pluginUiStateInvalidatedSubscription = eventBus.Subscribe<PluginUiStateInvalidatedCoreEvent>(OnPluginUiStateInvalidated);
    }

    public SettingsViewModel Settings { get; }

    public CommandCenterViewModel CommandCenter { get; }

    public ObservableCollection<CommandListItemViewModel> QuickCommands { get; } = new();

    public ObservableCollection<CommandListItemViewModel> SearchCommands { get; } = new();

    public ObservableCollection<string> CommandGroupOptions { get; } = new();

    public ObservableCollection<CommandGroupFilterOption> CommandGroupFilters { get; } = new();

    public ObservableCollection<TransmitTargetListItemViewModel> TransmitTargets { get; } = new();

    public Session? ActiveSession
    {
        get => _activeSession;
        private set => SetProperty(ref _activeSession, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public bool IsSendHexMode
    {
        get => _isSendHexMode;
        set
        {
            if (SetProperty(ref _isSendHexMode, value))
            {
                OnPropertyChanged(nameof(SendModeLabel));
            }
        }
    }

    public bool IsAdvancedOptionsOpen
    {
        get => _isAdvancedOptionsOpen;
        set
        {
            if (SetProperty(ref _isAdvancedOptionsOpen, value))
            {
                OnPropertyChanged(nameof(AdvancedOptionsChevronIcon));
            }
        }
    }

    public bool ClearAfterSend
    {
        get => _clearAfterSend;
        set => SetProperty(ref _clearAfterSend, value);
    }

    public string MessageInput
    {
        get => _messageInput;
        set
        {
            if (!SetProperty(ref _messageInput, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanClearInput));
        }
    }

    public string SendModeLabel => IsSendHexMode ? "HEX" : "STR";

    public TransmitTargetListItemViewModel? SelectedTransmitTarget
    {
        get => _selectedTransmitTarget;
        set
        {
            if (SetProperty(ref _selectedTransmitTarget, value))
            {
                SendErrorText = null;
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public bool RequiresTransmitTarget
    {
        get => _requiresTransmitTarget;
        private set
        {
            if (SetProperty(ref _requiresTransmitTarget, value))
            {
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(IsTransmitTargetSelectorVisible));
            }
        }
    }

    public string? SendErrorText
    {
        get => _sendErrorText;
        private set
        {
            if (SetProperty(ref _sendErrorText, value))
            {
                OnPropertyChanged(nameof(HasSendError));
            }
        }
    }

    // i18n-ignore
    public string PinnedCommandCountText => $"{QuickCommands.Count} / {MaxPinnedCommands}";

    public string CommandSearchQuery
    {
        get => _commandSearchQuery;
        set
        {
            if (SetProperty(ref _commandSearchQuery, value))
            {
                if (!_isSelectingSearchCommand)
                {
                    SelectedSearchCommand = null;
                }

                IsCommandSearchOpen = true;
                SyncSearchCommands();
            }
        }
    }

    public CommandGroupFilterOption? SelectedCommandGroupFilter
    {
        get => _selectedCommandGroupFilter;
        set
        {
            if (SetProperty(ref _selectedCommandGroupFilter, value))
            {
                SelectedSearchCommand = null;
                IsCommandSearchOpen = true;
                SyncSearchCommands();
            }
        }
    }

    public CommandListItemViewModel? SelectedSearchCommand
    {
        get => _selectedSearchCommand;
        private set
        {
            if (SetProperty(ref _selectedSearchCommand, value))
            {
                OnPropertyChanged(nameof(CanSendSelectedSearchCommand));
                OnPropertyChanged(nameof(CanPinSelectedSearchCommand));
                OnPropertyChanged(nameof(SelectedCommandDisplayText));
                OnPropertyChanged(nameof(SelectedCommandPayloadText));
                OnPropertyChanged(nameof(HasSelectedCommand));
            }
        }
    }

    public bool IsCommandSearchOpen
    {
        get => _isCommandSearchOpen;
        set
        {
            if (SetProperty(ref _isCommandSearchOpen, value))
            {
                OnPropertyChanged(nameof(ShowCommandSearchSuggestions));
                OnPropertyChanged(nameof(IsCommandSelectionVisible));
            }
        }
    }

    public string AdvancedOptionsChevronIcon =>
        IsAdvancedOptionsOpen
            ? "M18 15l-6-6-6 6"
            : "M6 9l6 6 6-6";

    public ToolDockTab SelectedToolTab
    {
        get => _selectedToolTab;
        set
        {
            if (!SetProperty(ref _selectedToolTab, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSendTabActive));
            OnPropertyChanged(nameof(IsCommandsTabActive));

            CommandCenter.IsActive = IsCommandsTabActive;
        }
    }

    public bool IsSendTabActive => _selectedToolTab == ToolDockTab.Send;
    public bool IsCommandsTabActive => _selectedToolTab == ToolDockTab.Commands;
    public bool CanSend
        => IsConnected
           && _activeSession?.InitializationState == SessionInitializationState.Ready
           && (!RequiresTransmitTarget || SelectedTransmitTarget is not null)
           && !string.IsNullOrWhiteSpace(MessageInput);
    public bool CanClearInput => !string.IsNullOrWhiteSpace(MessageInput);
    public bool HasSendError => !string.IsNullOrWhiteSpace(SendErrorText);
    public bool IsTransmitTargetSelectorVisible => RequiresTransmitTarget || TransmitTargets.Count > 0;

    public bool HasQuickCommands => QuickCommands.Count > 0;
    public bool HasSearchCommands => SearchCommands.Count > 0;
    public bool ShowCommandSearchSuggestions => IsCommandSearchOpen && HasSearchCommands;
    public bool IsCommandSelectionVisible => !IsCommandSearchOpen;
    public bool CanSendCommand
        => IsConnected
           && _activeSession?.InitializationState == SessionInitializationState.Ready;
    public bool CanSendSelectedSearchCommand => CanSendCommand && SelectedSearchCommand is not null;
    public bool CanPinSelectedSearchCommand => SelectedSearchCommand is not null;
    public bool CanOpenCommandEditor => CommandCenter.Commands.Count > 0;
    public string SelectedCommandDisplayText => SelectedSearchCommand?.Name ?? L["tool.commands.select"];
    public string SelectedCommandPayloadText => SelectedSearchCommand?.PayloadPreview ?? string.Empty;
    public bool HasSelectedCommand => SelectedSearchCommand is not null;

    public bool IsCommandEditorOpen
    {
        get => _isCommandEditorOpen;
        set => SetProperty(ref _isCommandEditorOpen, value);
    }

    public void SetActiveSession(Session? session)
    {
        if (!ReferenceEquals(_activeSession, session) && _activeSession is not null)
        {
            _activeSession.PropertyChanged -= OnActiveSessionPropertyChanged;
        }

        ActiveSession = session;

        if (_activeSession is not null)
        {
            _activeSession.PropertyChanged += OnActiveSessionPropertyChanged;
        }

        IsConnected = _activeSession?.Status == SessionStatus.Connected;
        ResetTransmitTargets();
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSendCommand));
        OnPropertyChanged(nameof(CanSendSelectedSearchCommand));
        CommandCenter.SetSession(session?.Id, session?.Name);
        CommandCenter.IsActive = IsCommandsTabActive;
        SyncQuickCommands();
        SyncSearchCommands();
        _ = RefreshTransmitTargetsAsync();
    }

    public void ToggleSendMode() => IsSendHexMode = !IsSendHexMode;

    public void ToggleAdvancedOptions() => IsAdvancedOptionsOpen = !IsAdvancedOptionsOpen;

    private void OnActiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _activeSession))
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(Session.Status), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(Session.InitializationState), StringComparison.Ordinal))
        {
            IsConnected = _activeSession?.Status == SessionStatus.Connected;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSendCommand));
            OnPropertyChanged(nameof(CanSendSelectedSearchCommand));
        }
    }

    public async Task SendAsync(bool hex, bool addCr, bool addLf)
    {
        if (ActiveSession == null || !CanSend)
        {
            return;
        }

        try
        {
            var format = hex ? MessageFormat.Hex : MessageFormat.Text;
            var result = await _workspaceCoordinator.SendMessageAsync(
                ActiveSession.Id,
                MessageInput,
                format,
                addCr,
                addLf,
                SelectedTransmitTarget?.Id);

            if (!result.Ok)
            {
                ApplySendFailure(result);
                return;
            }

            SendErrorText = null;
            if (ClearAfterSend)
            {
                MessageInput = string.Empty;
            }
        }
        catch (Exception ex)
        {
            // i18n-ignore (log message)
            _appLogService.LogException(ex, "Send failed");
            SendErrorText = ex.Message;
        }
    }

    public void ClearMessages()
    {
        if (ActiveSession == null)
        {
            return;
        }

        _workspaceCoordinator.ClearMessages(ActiveSession.Id);
        _messageStream.ClearView();
    }

    public async Task ExportAsync(string? filePath = null, SessionLogExportFormat? format = null)
    {
        if (ActiveSession == null)
        {
            return;
        }

        try
        {
            await _workspaceCoordinator.ExportAsync(ActiveSession, _messageStream.SearchQuery, filePath, format);
        }
        catch (Exception ex)
        {
            // i18n-ignore (log message)
            _appLogService.LogException(ex, "Export failed");
        }
    }

    public async Task SendCommandAsync(CommandDefinition? command)
    {
        if (command == null || ActiveSession == null || !CanSendCommand)
        {
            return;
        }

        CommandCenter.SelectedCommand = command;
        await CommandCenter.SendCommandAsync(command);
    }

    public void OpenCommandSearch()
    {
        IsCommandSearchOpen = true;
        _isSelectingSearchCommand = true;
        try
        {
            CommandSearchQuery = string.Empty;
        }
        finally
        {
            _isSelectingSearchCommand = false;
        }

        SyncSearchCommands();
    }

    public void CloseCommandSearch()
    {
        IsCommandSearchOpen = false;
    }

    public void SelectSearchCommand(CommandListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _isSelectingSearchCommand = true;
        try
        {
            CommandSearchQuery = item.Name;
        }
        finally
        {
            _isSelectingSearchCommand = false;
        }

        SelectedSearchCommand = item;
        IsCommandSearchOpen = false;
    }

    public async Task SendSelectedSearchCommandAsync()
    {
        if (SelectedSearchCommand is null)
        {
            return;
        }

        await SendCommandAsync(SelectedSearchCommand.Command);
    }

    public async Task PinSearchCommandAsync(CommandListItemViewModel? item)
    {
        if (item is null || item.Command.IsPinned)
        {
            return;
        }

        if (CommandCenter.Commands.Count(c => c.IsPinned) >= MaxPinnedCommands)
        {
            await CommandCenter.NotifyPinnedLimitAsync(MaxPinnedCommands);
            return;
        }

        item.Command.IsPinned = true;
        await CommandCenter.SaveCommandAsync(item.Command);
        if (ReferenceEquals(SelectedSearchCommand?.Command, item.Command))
        {
            SelectedSearchCommand = null;
        }

        SyncQuickCommands();
        SyncSearchCommands();
    }

    public async Task PinSelectedSearchCommandAsync()
        => await PinSearchCommandAsync(SelectedSearchCommand);

    public async Task UnpinCommandEditorAsync()
    {
        if (CommandCenter.SelectedCommand is null || !CommandCenter.SelectedCommand.IsPinned)
        {
            return;
        }

        CommandCenter.SelectedCommand.IsPinned = false;
        await CommandCenter.SaveCommandAsync(CommandCenter.SelectedCommand);
        SyncQuickCommands();
        SyncSearchCommands();
        IsCommandEditorOpen = false;
    }

    public void OpenCommandEditor(CommandDefinition? command = null)
    {
        if (command is null)
        {
            CommandCenter.NewCommand();
        }
        else
        {
            CommandCenter.SelectedCommand = command;
        }

        IsCommandEditorOpen = true;
    }

    public void CloseCommandEditor()
    {
        IsCommandEditorOpen = false;
    }

    public async Task SaveCommandEditorAsync()
    {
        await CommandCenter.SaveAsync();
        SyncQuickCommands();
        SyncCommandGroups();
        SyncSearchCommands();
        IsCommandEditorOpen = false;
    }

    public async Task DeleteCommandEditorAsync()
    {
        await CommandCenter.DeleteSelectedAsync();
        SyncQuickCommands();
        SyncCommandGroups();
        SyncSearchCommands();
        IsCommandEditorOpen = false;
    }

    public void ClearInput()
    {
        MessageInput = string.Empty;
    }

    private void OnCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncQuickCommands();
        SyncCommandGroups();
        SyncSearchCommands();
    }

    private void SyncQuickCommands()
    {
        QuickCommands.Clear();
        foreach (var command in CommandCenter.Commands
                     .Where(c => c.IsPinned)
                     .OrderBy(c => c.SortOrder)
                     .ThenBy(c => c.Name)
                     .Take(MaxPinnedCommands))
        {
            QuickCommands.Add(new CommandListItemViewModel(command));
        }

        OnPropertyChanged(nameof(HasQuickCommands));
        OnPropertyChanged(nameof(CanOpenCommandEditor));
        OnPropertyChanged(nameof(PinnedCommandCountText));
    }

    private void SyncCommandGroups()
    {
        var previous = SelectedCommandGroupFilter?.Value ?? string.Empty;
        CommandGroupOptions.Clear();
        CommandGroupFilters.Clear();
        CommandGroupFilters.Add(new CommandGroupFilterOption(string.Empty, L["tool.commands.allGroups"]));

        foreach (var group in CommandCenter.Commands
                     .Select(c => c.Group)
                     .Where(g => !string.IsNullOrWhiteSpace(g))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g))
        {
            CommandGroupOptions.Add(group);
            CommandGroupFilters.Add(new CommandGroupFilterOption(group, group));
        }

        _selectedCommandGroupFilter = CommandGroupFilters.FirstOrDefault(g => string.Equals(g.Value, previous, StringComparison.Ordinal))
                                      ?? CommandGroupFilters[0];
        OnPropertyChanged(nameof(SelectedCommandGroupFilter));
    }

    private void SyncSearchCommands()
    {
        SearchCommands.Clear();
        var query = CommandSearchQuery.Trim();

        var commands = CommandCenter.Commands.AsEnumerable();
        var selectedGroup = SelectedCommandGroupFilter?.Value ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedGroup))
        {
            commands = commands.Where(c => string.Equals(c.Group, selectedGroup, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            commands = commands.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Payload.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Group.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var command in commands
                     .Where(c => !c.IsPinned)
                     .OrderBy(c => c.Group)
                     .ThenBy(c => c.SortOrder)
                     .ThenBy(c => c.Name)
                     .Take(8))
        {
            SearchCommands.Add(new CommandListItemViewModel(command));
        }

        OnPropertyChanged(nameof(HasSearchCommands));
        OnPropertyChanged(nameof(ShowCommandSearchSuggestions));
        OnPropertyChanged(nameof(CanSendSelectedSearchCommand));
        OnPropertyChanged(nameof(CanPinSelectedSearchCommand));
    }

    private async Task RefreshTransmitTargetsAsync()
    {
        var session = ActiveSession;
        if (session is null || session.Status != SessionStatus.Connected)
        {
            ResetTransmitTargets();
            return;
        }

        var version = ++_transmitTargetRefreshVersion;
        PluginTransmitTargetSnapshot snapshot;
        try
        {
            snapshot = await _workspaceCoordinator.GetTransmitTargetsAsync(session.Id);
        }
        catch (Exception ex)
        {
            // i18n-ignore (log message)
            _appLogService.LogException(ex, "Refresh transmit targets failed");
            snapshot = new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != _transmitTargetRefreshVersion
                || ActiveSession is null
                || !string.Equals(ActiveSession.Id, session.Id, StringComparison.Ordinal))
            {
                return;
            }

            ApplyTransmitTargets(snapshot);
        });
    }

    private void ApplyTransmitTargets(PluginTransmitTargetSnapshot snapshot)
    {
        var previousId = SelectedTransmitTarget?.Id;
        TransmitTargets.Clear();
        foreach (var target in snapshot.Targets)
        {
            TransmitTargets.Add(new TransmitTargetListItemViewModel(target));
        }

        RequiresTransmitTarget = snapshot.RequireTargetForSend;
        SelectedTransmitTarget =
            TransmitTargets.FirstOrDefault(t => string.Equals(t.Id, previousId, StringComparison.Ordinal))
            ?? TransmitTargets.FirstOrDefault(t => string.Equals(t.Id, snapshot.DefaultTargetId, StringComparison.Ordinal))
            ?? (RequiresTransmitTarget ? TransmitTargets.FirstOrDefault() : null);

        OnPropertyChanged(nameof(IsTransmitTargetSelectorVisible));
        OnPropertyChanged(nameof(CanSend));
    }

    private void ResetTransmitTargets()
    {
        _transmitTargetRefreshVersion++;
        TransmitTargets.Clear();
        SelectedTransmitTarget = null;
        RequiresTransmitTarget = false;
        SendErrorText = null;
        OnPropertyChanged(nameof(IsTransmitTargetSelectorVisible));
        OnPropertyChanged(nameof(CanSend));
    }

    private void ApplySendFailure(PluginCommandResult result)
    {
        SendErrorText = result.ErrorCode switch
        {
            "target-required" => L["tool.send.error.targetRequired"],
            "target-not-found" => L["tool.send.error.targetNotFound"],
            _ => result.Error ?? L["tool.send.error.generic"]
        };

        if (result.TargetInvalidated)
        {
            _ = RefreshTransmitTargetsAsync();
        }
    }

    private void OnPluginUiStateInvalidated(PluginUiStateInvalidatedCoreEvent evt)
    {
        var sessionId = ActiveSession?.Id;
        if (string.IsNullOrWhiteSpace(sessionId)
            || !string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(evt.ViewKind, "transmit-targets", StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = RefreshTransmitTargetsAsync());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pluginUiStateInvalidatedSubscription.Dispose();
            CommandCenter.Commands.CollectionChanged -= OnCommandsChanged;
        }

        base.Dispose(disposing);
    }
}

public sealed class CommandListItemViewModel
{
    public CommandListItemViewModel(CommandDefinition command)
    {
        Command = command;
    }

    public CommandDefinition Command { get; }

    public string Name => Command.Name;

    public string Group => Command.Group;

    public bool IsPinned => Command.IsPinned;

    public string PayloadPreview
    {
        get
        {
            var payload = Command.Payload
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

            if (string.IsNullOrEmpty(payload))
            {
                if (Command.AppendCr && Command.AppendLf)
                {
                    return "\\r\\n";
                }

                if (Command.AppendCr)
                {
                    return "\\r";
                }

                if (Command.AppendLf)
                {
                    return "\\n";
                }
            }

            return payload;
        }
    }
}

public sealed record CommandGroupFilterOption(string Value, string Label);

public sealed class TransmitTargetListItemViewModel
{
    public TransmitTargetListItemViewModel(PluginTransmitTarget target)
    {
        Target = target;
    }

    public PluginTransmitTarget Target { get; }
    public string Id => Target.Id;
    public string DisplayName => Target.DisplayName;
    public string Subtitle => Target.Subtitle ?? string.Empty;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Target.Subtitle);
}
