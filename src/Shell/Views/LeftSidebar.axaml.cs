using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.Services;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ComCross.Shell.Views;

public partial class LeftSidebar : BaseUserControl
{
    public LeftSidebar()
    {
        InitializeComponent();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (owner.DataContext is MainWindowViewModel vm)
        {
            await ShellUiServices.ConnectDialogService.ShowAsync(owner, vm.PluginManager);
        }
    }

    private void OnSessionSettingsClick(object? sender, RoutedEventArgs e)
    {
        OpenSessionDetailFromSender(sender, openReconnectEditor: false);
    }

    private void OnSessionConnectionParametersClick(object? sender, RoutedEventArgs e)
    {
        OpenSessionDetailFromSender(sender, openReconnectEditor: true);
    }

    private void OpenSessionDetailFromSender(object? sender, bool openReconnectEditor)
    {
        if (sender is not Button button)
        {
            return;
        }

        var session = button.DataContext switch
        {
            Session s => s,
            SessionListItemViewModel itemVm => itemVm.Session,
            LeftSidebarViewModel sidebarVm => sidebarVm.ActiveSession,
            _ => null
        };

        if (session is null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner || owner.DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        vm.OpenSessionDetail(session, openReconnectEditor);
    }

    private void OnToggleParentCollapseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var itemVm = button.DataContext as SessionListItemViewModel;
        if (itemVm is null || !itemVm.HasChildSessions)
        {
            return;
        }

        if (DataContext is LeftSidebarViewModel sidebarVm)
        {
            sidebarVm.ToggleParentCollapsed(itemVm.Session.Id);
        }
    }

    private async void OnRenameSessionClick(object? sender, RoutedEventArgs e)
    {
        if (GetSessionFromSender(sender) is not { } session)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner || owner.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await ShowRenameDialogAsync(session, vm);
    }

    private async void OnDeleteSessionClick(object? sender, RoutedEventArgs e)
    {
        if (GetSessionFromSender(sender) is not { } session)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner || owner.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var hasChildren = DataContext is LeftSidebarViewModel sidebarVmForMessage
                          && sidebarVmForMessage.GetChildSessionCount(session.Id) > 0;
        var messageKey = hasChildren
            ? "dialog.deleteSession.listener.message"
            : "dialog.deleteSession.message";
        var confirmed = await MessageBoxService.ShowConfirmAsync(
            vm.L["dialog.deleteSession.title"],
            string.Format(vm.L[messageKey], session.Name));
        if (!confirmed)
        {
            return;
        }

        if (DataContext is LeftSidebarViewModel sidebarVm)
        {
            await sidebarVm.DeleteSessionAsync(session.Id);
        }
    }

    private static Session? GetSessionFromSender(object? sender)
    {
        if (sender is not Control control)
        {
            return null;
        }

        return control.DataContext switch
        {
            Session s => s,
            SessionListItemViewModel itemVm => itemVm.Session,
            _ => null
        };
    }

    private async Task ShowRenameDialogAsync(Session session, MainWindowViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var result = await ShellUiServices.SessionRenameDialogService.ShowAsync(owner, session, vm);
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (DataContext is LeftSidebarViewModel sidebarVm)
            {
                await sidebarVm.RenameSessionAsync(session.Id, result);
            }
        }
    }
}
