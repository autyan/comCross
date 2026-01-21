using Avalonia.Interactivity;

namespace ComCross.Shell.Views;

public partial class ConnectDialog : BaseWindow
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
