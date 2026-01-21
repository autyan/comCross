using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ComCross.Shell.Services;
using ComCross.PluginSdk.UI;

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
            var dialog = App.ServiceProvider.GetRequiredService<ConnectDialog>();
            var objectFactory = App.ServiceProvider.GetRequiredService<IObjectFactory>();
            // Create a dedicated selector VM for the dialog so its rendered controls
            // are not shared with the sidebar's visual tree.
            var viewInstanceId = Guid.NewGuid().ToString("N");
            var selectorVm = objectFactory.Create<BusAdapterSelectorViewModel>(vm.PluginManager, BusAdapterSelectorViewModel.BusAdapterViewKind, viewInstanceId);
            dialog.DataContext = objectFactory.Create<ConnectDialogViewModel>(vm.PluginManager, selectorVm);

            await dialog.ShowDialog(owner);

            // Clean up per-instance registrations; shared state is preserved by ViewKind.
            var stateManager = App.ServiceProvider.GetRequiredService<PluginUiStateManager>();
            stateManager.ClearViewScope(new PluginUiViewScope(BusAdapterSelectorViewModel.BusAdapterViewKind, viewInstanceId));
        }
    }
    
    private void OnSessionSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Session session)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner || owner.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var flyout = new MenuFlyout();
        
        var renameItem = new MenuItem { Header = vm.L["session.menu.rename"] };
        renameItem.Click += async (s, args) => await ShowRenameDialogAsync(session, vm);
        
        var deleteItem = new MenuItem { Header = vm.L["session.menu.delete"] };
        deleteItem.Click += async (s, args) =>
        {
            var newActive = await vm.SessionsVm.DeleteSessionAsync(vm.Sessions, vm.ActiveSession, session.Id);
            vm.ActiveSession = newActive;
        };
        
        flyout.Items.Add(renameItem);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(deleteItem);
        
        flyout.ShowAt(button);
    }
    
    private async Task ShowRenameDialogAsync(Session session, MainWindowViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialogFactory = App.ServiceProvider.GetRequiredService<ITextInputDialogFactory>();
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
