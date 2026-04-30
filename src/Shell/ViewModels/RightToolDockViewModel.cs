using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class RightToolDockViewModel : BaseViewModel
{
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly AppLogService _appLogService;
    private readonly MessageStreamViewModel _messageStream;

    private Session? _activeSession;
    private bool _isConnected;
    private bool _isSendHexMode;
    private bool _isAdvancedOptionsOpen;
    private bool _clearAfterSend;
    private string _messageInput = string.Empty;
    private ToolDockTab _selectedToolTab = ToolDockTab.Send;

    public RightToolDockViewModel(
        ILocalizationService localization,
        IWorkspaceCoordinator workspaceCoordinator,
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
        SyncQuickCommands();
    }

    public SettingsViewModel Settings { get; }

    public CommandCenterViewModel CommandCenter { get; }

    public ObservableCollection<CommandDefinition> QuickCommands { get; } = new();

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
    public bool CanSend => IsConnected && !string.IsNullOrWhiteSpace(MessageInput);
    public bool CanClearInput => !string.IsNullOrWhiteSpace(MessageInput);

    public bool HasQuickCommands => QuickCommands.Count > 0;
    public bool CanOpenCommandEditor => CommandCenter.Commands.Count > 0;

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
        OnPropertyChanged(nameof(CanSend));
        CommandCenter.SetSession(session?.Id, session?.Name);
        CommandCenter.IsActive = IsCommandsTabActive;
        SyncQuickCommands();
    }

    public void ToggleSendMode() => IsSendHexMode = !IsSendHexMode;

    public void ToggleAdvancedOptions() => IsAdvancedOptionsOpen = !IsAdvancedOptionsOpen;

    private void OnActiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _activeSession))
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(Session.Status), StringComparison.Ordinal))
        {
            IsConnected = _activeSession?.Status == SessionStatus.Connected;
            OnPropertyChanged(nameof(CanSend));
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
            await _workspaceCoordinator.SendMessageAsync(ActiveSession.Id, MessageInput, format, addCr, addLf);

            if (ClearAfterSend)
            {
                MessageInput = string.Empty;
            }
        }
        catch (Exception ex)
        {
            // i18n-ignore (log message)
            _appLogService.LogException(ex, "Send failed");
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

    public async Task ExportAsync(string? filePath = null)
    {
        if (ActiveSession == null)
        {
            return;
        }

        try
        {
            await _workspaceCoordinator.ExportAsync(ActiveSession, _messageStream.SearchQuery, filePath);
        }
        catch (Exception ex)
        {
            // i18n-ignore (log message)
            _appLogService.LogException(ex, "Export failed");
        }
    }

    public async Task SendCommandAsync(CommandDefinition? command)
    {
        if (command == null || ActiveSession == null || !IsConnected)
        {
            return;
        }

        CommandCenter.SelectedCommand = command;
        await CommandCenter.SendSelectedAsync();
    }

    public void OpenCommandEditor()
    {
        SelectedToolTab = ToolDockTab.Commands;
    }

    public void ClearInput()
    {
        MessageInput = string.Empty;
    }

    private void OnCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncQuickCommands();
    }

    private void SyncQuickCommands()
    {
        QuickCommands.Clear();
        foreach (var command in CommandCenter.Commands.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Take(3))
        {
            QuickCommands.Add(command);
        }

        OnPropertyChanged(nameof(HasQuickCommands));
        OnPropertyChanged(nameof(CanOpenCommandEditor));
    }
}
