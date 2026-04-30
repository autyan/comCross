using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;

namespace ComCross.Shell.Views;

public partial class RightToolDock : BaseUserControl
{
    public RightToolDock()
    {
        InitializeComponent();
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            var addCr = this.FindControl<CheckBox>("AddCrCheckBox");
            var addLf = this.FindControl<CheckBox>("AddLfCheckBox");

            await vm.SendAsync(
                vm.IsSendHexMode,
                addCr?.IsChecked ?? false,
                addLf?.IsChecked ?? false);
        }
    }

    private void OnToggleSendMode(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.ToggleSendMode();
            e.Handled = true;
        }
    }

    private async void OnQuickCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm && sender is Button { DataContext: CommandDefinition command })
        {
            await vm.SendCommandAsync(command);
        }
    }

    private void OnOpenAllCommandsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.OpenCommandEditor();
        }
    }

    private void OnClearInputClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.ClearInput();
        }
    }

    private void OnToggleAdvancedOptionsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.ToggleAdvancedOptions();
        }
    }

    private void OnBackToSendClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.SelectedToolTab = ToolDockTab.Send;
        }
    }

    private async void OnSendMessageBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || DataContext is not RightToolDockViewModel vm)
        {
            return;
        }

        var addCr = this.FindControl<CheckBox>("AddCrCheckBox");
        var addLf = this.FindControl<CheckBox>("AddLfCheckBox");

        await vm.SendAsync(
            vm.IsSendHexMode,
            addCr?.IsChecked ?? false,
            addLf?.IsChecked ?? false);
        e.Handled = true;
    }
}
