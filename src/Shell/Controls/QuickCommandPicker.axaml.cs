using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Controls;

public partial class QuickCommandPicker : UserControl
{
    public QuickCommandPicker()
    {
        InitializeComponent();
    }

    private void OnCommandSearchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            PickerInput.Classes.Add("searching");
            vm.OpenCommandSearch();
            CommandSearchBox.Focus();
            e.Handled = true;
        }
    }

    private void OnSelectSearchCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm && sender is Button { DataContext: CommandListItemViewModel item })
        {
            vm.SelectSearchCommand(item);
            CloseSearchVisualState();
        }
    }

    private async void OnSendSelectedCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm)
        {
            await vm.SendSelectedSearchCommandAsync();
        }
    }

    private async void OnPinSearchCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RightToolDockViewModel vm && sender is Button { DataContext: CommandListItemViewModel item })
        {
            await vm.PinSearchCommandAsync(item);
            e.Handled = true;
        }
    }

    private void OnCommandSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is RightToolDockViewModel vm)
        {
            CloseSearch(vm);
            e.Handled = true;
        }
    }

    private void OnSuggestionsPopupClosed(object? sender, EventArgs e)
    {
        if (DataContext is not RightToolDockViewModel vm)
        {
            return;
        }

        CloseSearch(vm);
    }

    private void CloseSearch(RightToolDockViewModel vm)
    {
        vm.CloseCommandSearch();
        CloseSearchVisualState();
    }

    private void CloseSearchVisualState()
    {
        PickerInput.Classes.Remove("searching");
    }
}
