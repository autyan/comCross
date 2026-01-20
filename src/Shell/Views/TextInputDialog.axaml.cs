using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class TextInputDialog : BaseWindow
{
    public TextInputDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var box = this.FindControl<TextBox>("InputBox");
        box?.Focus();
        box?.SelectAll();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close(null);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextInputDialogViewModel vm)
        {
            Close(null);
            return;
        }

        var text = vm.Text?.Trim();
        Close(string.IsNullOrWhiteSpace(text) ? null : text);
    }
}
