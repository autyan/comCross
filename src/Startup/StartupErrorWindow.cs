using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ComCross.Startup;

internal sealed class StartupErrorWindow : Window
{
    private readonly string _logDirectory;

    public StartupErrorWindow(string title, string message, string logDirectory)
    {
        _logDirectory = logDirectory;

        Title = title;
        Width = 520;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        Icon = new WindowIcon("app-icon.ico");

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };

        var logPathBlock = new TextBlock
        {
            Text = $"Log folder: {logDirectory}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.8
        };

        var openButton = new Button
        {
            Content = "Open log folder",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 128
        };
        openButton.Click += (_, _) => OpenLogFolder();

        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 96
        };
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { openButton, closeButton }
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children = { titleBlock, messageBlock, logPathBlock, buttons }
        };
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            Process.Start(new ProcessStartInfo(_logDirectory) { UseShellExecute = true });
        }
        catch
        {
            // The error dialog is already showing the log path.
        }
    }
}
