using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.PluginSdk.UI;
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
            var objectFactory = ShellUiServices.ObjectFactory;
            var dialog = objectFactory.Create<ConnectDialog>();
            var viewInstanceId = Guid.NewGuid().ToString("N");
            var selectorVm = objectFactory.Create<BusAdapterSelectorViewModel>(vm.PluginManager, BusAdapterSelectorViewModel.BusAdapterViewKind, viewInstanceId);
            dialog.DataContext = objectFactory.Create<ConnectDialogViewModel>(vm.PluginManager, selectorVm);

            await dialog.ShowDialog(owner);

            var stateManager = ShellUiServices.PluginUiStateManager;
            stateManager.ClearViewScope(new PluginUiViewScope(BusAdapterSelectorViewModel.BusAdapterViewKind, viewInstanceId));
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

    private void OnToggleListenerCollapseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var itemVm = button.DataContext as SessionListItemViewModel;
        if (itemVm is null || !itemVm.IsListener)
        {
            return;
        }

        if (DataContext is LeftSidebarViewModel sidebarVm)
        {
            sidebarVm.ToggleListenerCollapsed(itemVm.Session.Id);
        }
    }

    private async Task ShowRenameDialogAsync(Session session, MainWindowViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialogFactory = ShellUiServices.TextInputDialogFactory;
        var result = await dialogFactory.ShowAsync(
            owner,
            vm.Localization,
            vm.L["dialog.renameSession.title"],
            vm.L["dialog.renameSession.label"],
            session.Name,
            vm.L["dialog.renameSession.placeholder"],
            vm.L["dialog.renameSession.ok"],
            vm.L["dialog.renameSession.cancel"]);
        if (!string.IsNullOrWhiteSpace(result))
        {
            session.Name = result;
        }
    }
}
