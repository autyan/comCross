using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
