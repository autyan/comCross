using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using ComCross.Shell.Controls;
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
            var addCr = this.FindControl<SwitchCheckBox>("AddCrCheckBox");
            var addLf = this.FindControl<SwitchCheckBox>("AddLfCheckBox");

            await vm.SendAsync(
                vm.IsSendHexMode,
                addCr?.IsChecked ?? false,
                addLf?.IsChecked ?? false);
        }
    }

    private void OnSendModeToggleRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.ToggleSendMode();
            e.Handled = true;
        }
    }

    private async void OnQuickCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm && sender is Button { DataContext: CommandListItemViewModel item })
        {
            await vm.SendCommandAsync(item.Command);
        }
    }

    private void OnEditCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm && sender is Button { DataContext: CommandListItemViewModel item })
        {
            vm.OpenCommandEditor(item.Command);
        }
    }

    private void OnNewCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.OpenCommandEditor();
        }
    }

    private void OnCancelCommandEditorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            vm.CloseCommandEditor();
        }
    }

    private async void OnSaveCommandEditorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            await vm.SaveCommandEditorAsync();
        }
    }

    private async void OnDeleteCommandEditorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            await vm.DeleteCommandEditorAsync();
        }
    }

    private async void OnUnpinCommandEditorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            await vm.UnpinCommandEditorAsync();
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

        var addCr = this.FindControl<SwitchCheckBox>("AddCrCheckBox");
        var addLf = this.FindControl<SwitchCheckBox>("AddLfCheckBox");

        await vm.SendAsync(
            vm.IsSendHexMode,
            addCr?.IsChecked ?? false,
            addLf?.IsChecked ?? false);
        e.Handled = true;
    }
}
