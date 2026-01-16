using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;
using System.Linq;
using System.Threading.Tasks;

namespace ComCross.Shell.Views;

public partial class LeftSidebar : UserControl
{
    public static readonly StyledProperty<LocalizedStringsViewModel?> LocalizedStringsProperty =
        AvaloniaProperty.Register<LeftSidebar, LocalizedStringsViewModel?>(nameof(LocalizedStrings));

    public LocalizedStringsViewModel? LocalizedStrings
    {
        get => GetValue(LocalizedStringsProperty);
        set => SetValue(LocalizedStringsProperty, value);
    }

    public LeftSidebar()
    {
        InitializeComponent();
    }
    
    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.QuickConnectAsync();
        }
    }
    
    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.DisconnectAsync();
        }
    }
    
    private async void OnRefreshPortsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RefreshDevicesAsync();
        }
    }
    
    private void OnSessionSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is Session session && DataContext is MainWindowViewModel vm)
        {
            var flyout = new MenuFlyout();
            
            var renameItem = new MenuItem { Header = vm.LocalizedStrings.SessionMenuRename };
            renameItem.Click += async (s, args) => await ShowRenameDialogAsync(session, vm);
            
            var deleteItem = new MenuItem { Header = vm.LocalizedStrings.SessionMenuDelete };
            deleteItem.Click += async (s, args) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    await viewModel.DeleteSessionAsync(session.Id);
                }
            };
            
            flyout.Items.Add(renameItem);
            flyout.Items.Add(new Separator());
            flyout.Items.Add(deleteItem);
            
            flyout.ShowAt(button);
        }
    }
    
    private async Task ShowRenameDialogAsync(Session session, MainWindowViewModel vm)
        {
            var dialog = new Window
            {
                Title = vm.LocalizedStrings.SessionRenameTitle,
                Width = 350,
                Height = 150,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var textBox = new TextBox
            {
                Text = session.Name,
                Watermark = vm.LocalizedStrings.SessionRenamePlaceholder,
                Margin = new Avalonia.Thickness(0, 0, 0, 10)
            };

            var okButton = new Button
            {
                Content = vm.LocalizedStrings.SessionRenameOk,
                Width = 80,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 0, 10, 0)
            };

            var cancelButton = new Button
            {
                Content = vm.LocalizedStrings.SessionRenameCancel,
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
                    new TextBlock { Text = vm.LocalizedStrings.SessionRenameLabel, Margin = new Avalonia.Thickness(0, 0, 0, 5) },
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
