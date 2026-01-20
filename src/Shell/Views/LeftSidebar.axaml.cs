using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

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
            dialog.DataContext = vm;

            await dialog.ShowDialog(owner);
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
            var dialog = new Window
            {
                Title = vm.L["dialog.renameSession.title"],
                Width = 350,
                Height = 150,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var textBox = new TextBox
            {
                Text = session.Name,
                Watermark = vm.L["dialog.renameSession.placeholder"],
                Margin = new Avalonia.Thickness(0, 0, 0, 10)
            };

            var okButton = new Button
            {
                Content = vm.L["dialog.renameSession.ok"],
                Width = 80,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 0, 10, 0)
            };

            var cancelButton = new Button
            {
                Content = vm.L["dialog.renameSession.cancel"],
                Width = 80,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };

            okButton.Click += (s, args) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    session.Name = textBox.Text;
                }
                dialog.Close();
            };

            cancelButton.Click += (s, args) => dialog.Close();

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Children = { okButton, cancelButton }
            };

            var mainPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Children = 
                {
                    new TextBlock { Text = vm.L["dialog.renameSession.label"], Margin = new Avalonia.Thickness(0, 0, 0, 5) },
                    textBox,
                    buttonPanel
                }
            };

            dialog.Content = mainPanel;
            
            textBox.Focus();
            textBox.SelectAll();

            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                await dialog.ShowDialog(owner);
            }
    }
}
