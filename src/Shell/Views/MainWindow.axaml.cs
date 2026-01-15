using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Services;

namespace ComCross.Shell.Views;

public partial class MainWindow : Window
{
    private bool _isClosing = false;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Subscribe to window closing event to trigger cleanup
        Closing += OnWindowClosing;
    }

    private async void OnSessionNameClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.ActiveSession == null)
        {
            return;
        }

        var localization = vm.Localization;
        var dialog = new Window
        {
            Title = localization.GetString("session.edit.title"),
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var textBox = new TextBox
        {
            Text = vm.ActiveSession.Name,
            Watermark = localization.GetString("dialog.connect.sessionname.placeholder"),
            Margin = new Thickness(0, 8, 0, 16)
        };

        var okButton = new Button
        {
            Content = localization.GetString("session.edit.save"),
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var cancelButton = new Button
        {
            Content = localization.GetString("session.edit.cancel"),
            Width = 80
        };

        okButton.Click += async (s, e) =>
        {
            var newName = textBox.Text?.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                await vm.UpdateSessionNameAsync(newName);
            }
            dialog.Close();
        };

        cancelButton.Click += (s, e) => dialog.Close();

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { okButton, cancelButton }
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = localization.GetString("session.edit.name"), Margin = new Thickness(0, 0, 0, 4) },
                textBox,
                buttonPanel
            }
        };

        dialog.Content = content;

        // Focus textbox and select all text
        dialog.Opened += (s, e) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        await dialog.ShowDialog(this);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosing)
        {
            return; // Already processing close
        }

        // Cancel the default close behavior
        e.Cancel = true;
        _isClosing = true;

        // Run cleanup asynchronously
        if (DataContext is MainWindowViewModel vm)
        {
            await Task.Run(async () =>
            {
                await vm.CleanupWithProgressAsync();
            });
        }

        // Now actually close
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // Show connect dialog
            var dialog = new ConnectDialog
            {
                DataContext = vm
            };

            await dialog.ShowDialog(this);
        }
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.DisconnectAsync();
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearMessages();
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null)
            {
                _ = vm.ExportAsync();
                return;
            }

            var format = vm.Settings.ExportDefaultFormat;
            var suggestedName = $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{format}";
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = vm.LocalizedStrings.MenuExport,
                SuggestedFileName = suggestedName,
                DefaultExtension = format,
                FileTypeChoices =
                [
                    new FilePickerFileType("Export")
                    {
                        Patterns = ["*.txt", "*.json"]
                    }
                ]
            });

            if (file != null)
            {
                await vm.ExportAsync(file.Path.LocalPath);
            }
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleSettings();
        }
    }

    private void OnNotificationsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleNotifications();
        }
    }

    private void OnSettingsCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = false;
        }
    }

    private void OnNotificationsCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsNotificationsOpen = false;
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (e.Key == Key.Escape)
            {
                vm.IsSettingsOpen = false;
                vm.IsNotificationsOpen = false;
                return;
            }

            if (ShouldIgnoreHotkey(e.Source))
            {
                return;
            }

            if (IsModifierKey(e.Key))
            {
                return;
            }

            var gesture = new KeyGesture(e.Key, e.KeyModifiers);
            if (await vm.CommandCenter.TryExecuteHotkeyAsync(gesture.ToString()))
            {
                e.Handled = true;
            }
        }
    }

    private static bool ShouldIgnoreHotkey(object? source)
    {
        return source is TextBox or ComboBox or AutoCompleteBox;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }
}
