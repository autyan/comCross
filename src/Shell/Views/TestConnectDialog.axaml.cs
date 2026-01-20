using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class TestConnectDialog : BaseWindow
{
    public TestConnectDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close(null);

    private void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestConnectDialogViewModel vm)
        {
            Close(null);
            return;
        }

        if (vm.SelectedOption is null)
        {
            Close(null);
            return;
        }

        Close(new TestConnectDialogResult(vm.SelectedOption.Id, vm.ParametersJson));
    }
}

public sealed record TestConnectDialogResult(string CapabilityId, string? ParametersJson);
