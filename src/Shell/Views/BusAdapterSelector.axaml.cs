using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ComCross.Shell.Views;

public partial class BusAdapterSelector : UserControl
{
    public BusAdapterSelector()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
