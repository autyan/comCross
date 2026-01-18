using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class PluginManagerView : BaseUserControl
{
    public PluginManagerView()
    {
        InitializeComponent();
    }

    private async void OnTogglePluginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings)
        {
            return;
        }

        if (sender is CheckBox { DataContext: PluginItemViewModel plugin })
        {
            await settings.PluginManager.ToggleAsync(plugin);
        }
    }

    private async void OnTestConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings)
        {
            return;
        }

        if (sender is Button { DataContext: PluginItemViewModel plugin })
        {
            await ShowTestConnectDialogAsync(settings, plugin);
        }
    }

    private async Task ShowTestConnectDialogAsync(SettingsViewModel settings, PluginItemViewModel plugin)
    {
        var options = settings.PluginManager.GetCapabilityOptions(plugin.Id);
        if (options.Count == 0)
        {
            await settings.PluginManager.TestConnectAsync(plugin);
            return;
        }

        var dialog = new Window
        {
            Title = settings.L["settings.plugins.connectTest.dialog.title"],
            Width = 520,
            Height = 420,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var capabilityLabel = new TextBlock
        {
            Text = settings.L["settings.plugins.connectTest.dialog.capability"],
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var capabilityCombo = new ComboBox
        {
            ItemsSource = options,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        capabilityCombo.ItemTemplate = new FuncDataTemplate<CapabilityOption>((item, _) =>
        {
            return new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = item.Name },
                    new TextBlock { Text = item.Id, FontSize = 11, Foreground = Avalonia.Media.Brushes.Gray }
                }
            };
        });

        var paramsLabel = new TextBlock
        {
            Text = settings.L["settings.plugins.connectTest.dialog.parameters"],
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        };

        var paramsBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 220
        };

        ScrollViewer.SetVerticalScrollBarVisibility(paramsBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(paramsBox, ScrollBarVisibility.Auto);

        // Prefill with default params of the first capability.
        var first = (CapabilityOption?)capabilityCombo.SelectedItem;
        paramsBox.Text = string.IsNullOrWhiteSpace(first?.DefaultParametersJson) ? "{}" : first!.DefaultParametersJson;

        capabilityCombo.SelectionChanged += (_, _) =>
        {
            if (paramsBox.Text is null || string.IsNullOrWhiteSpace(paramsBox.Text))
            {
                var selected = (CapabilityOption?)capabilityCombo.SelectedItem;
                paramsBox.Text = string.IsNullOrWhiteSpace(selected?.DefaultParametersJson) ? "{}" : selected!.DefaultParametersJson;
            }
        };

        var connectBtn = new Button
        {
            Content = settings.L["settings.plugins.connectTest.dialog.connect"],
            Classes = { "accent" },
            MinWidth = 110
        };

        var cancelBtn = new Button
        {
            Content = settings.L["settings.plugins.connectTest.dialog.cancel"],
            MinWidth = 110
        };

        cancelBtn.Click += (_, _) => dialog.Close();
        connectBtn.Click += async (_, _) =>
        {
            var selected = (CapabilityOption?)capabilityCombo.SelectedItem;
            if (selected is null)
            {
                dialog.Close();
                return;
            }

            await settings.PluginManager.TestConnectAsync(plugin, selected.Id, paramsBox.Text);
            dialog.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelBtn, connectBtn }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Children =
            {
                capabilityLabel,
                capabilityCombo,
                paramsLabel,
                paramsBox,
                buttons
            }
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }
}
