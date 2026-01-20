using System;
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
    }

    public SettingsViewModel Settings { get; }

    public CommandCenterViewModel CommandCenter { get; }

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
        }
    }

    public bool IsSendTabActive => _selectedToolTab == ToolDockTab.Send;
    public bool IsCommandsTabActive => _selectedToolTab == ToolDockTab.Commands;

    public void SetActiveSession(Session? session)
    {
        ActiveSession = session;
        IsConnected = session?.Status == SessionStatus.Connected;
        CommandCenter.SetSession(session?.Id, session?.Name);
    }

    public async Task SendAsync(string message, bool hex, bool addCr, bool addLf)
    {
        if (ActiveSession == null || !IsConnected)
        {
            return;
        }

        try
        {
            var format = hex ? MessageFormat.Hex : MessageFormat.Text;
            await _workspaceCoordinator.SendMessageAsync(ActiveSession.Id, message, format, addCr, addLf);
        }
        catch (Exception ex)
        {
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
            _appLogService.LogException(ex, "Export failed");
        }
    }
}
