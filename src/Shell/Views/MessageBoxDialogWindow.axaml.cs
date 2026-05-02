using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ComCross.Shell.Views;

public partial class MessageBoxDialogWindow : BaseWindow
{
    public MessageBoxDialogWindow()
    {
        InitializeComponent();
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            Close(null);
            return;
        }

        if (button.Tag is int index)
        {
            Close(index);
            return;
        }

        if (button.Tag is string text && int.TryParse(text, out var parsed))
        {
            Close(parsed);
            return;
        }

        Close(null);
    }
}
