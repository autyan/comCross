using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class SessionsViewModel : BaseViewModel
{
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly AppLogService _appLogService;
    private readonly DisplaySettingsViewModel _display;

    public SessionsViewModel(
        ILocalizationService localization,
        IWorkspaceCoordinator workspaceCoordinator,
        AppLogService appLogService,
        DisplaySettingsViewModel display)
        : base(localization)
    {
        _workspaceCoordinator = workspaceCoordinator;
        _appLogService = appLogService;
        _display = display;
    }

    public async Task<Session?> DeleteSessionAsync(ObservableCollection<Session> sessions, Session? activeSession, string sessionId)
    {
        try
        {
            await _workspaceCoordinator.DeleteSessionAsync(sessionId);

            var session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is null)
            {
                return activeSession;
            }

            var wasActive = string.Equals(activeSession?.Id, sessionId, StringComparison.Ordinal);
            sessions.Remove(session);

            Session? newActive = activeSession;
            if (wasActive)
            {
                newActive = sessions.FirstOrDefault();
            }

            await _workspaceCoordinator.SaveCurrentStateAsync(sessions, newActive, _display.AutoScrollEnabled);
            return newActive;
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Delete session failed");
            return activeSession;
        }
    }
}
