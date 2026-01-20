using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComCross.Shell.ViewModels;
using System;

namespace ComCross.Shell.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

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
