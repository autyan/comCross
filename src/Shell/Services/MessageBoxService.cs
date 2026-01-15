using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComCross.Shared.Services;

namespace ComCross.Shell.Services;

/// <summary>
/// Service for showing message boxes without direct Window reference
/// </summary>
public static class MessageBoxService
{
    private static ILocalizationService? _localization;
    
    public static void Initialize(ILocalizationService localization)
    {
        _localization = localization;
    }
    
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    public static async Task ShowErrorAsync(string title, string message)
    {
        var okText = _localization?.GetString("messagebox.ok") ?? "OK";
        await ShowMessageAsync(title, message, MessageBoxIcon.Error, new[] { okText });
    }

    public static async Task ShowWarningAsync(string title, string message)
    {
        var okText = _localization?.GetString("messagebox.ok") ?? "OK";
        await ShowMessageAsync(title, message, MessageBoxIcon.Warning, new[] { okText });
    }

    public static async Task ShowInfoAsync(string title, string message)
    {
        var okText = _localization?.GetString("messagebox.ok") ?? "OK";
        await ShowMessageAsync(title, message, MessageBoxIcon.Info, new[] { okText });
    }

    public static async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var okText = _localization?.GetString("messagebox.ok") ?? "OK";
        var cancelText = _localization?.GetString("messagebox.cancel") ?? "Cancel";
        var result = await ShowCustomAsync(title, message, MessageBoxIcon.Question, okText, cancelText);
        return result == 0;
    }

    /// <summary>
    /// Show error dialog for serial port access denied with analysis option
    /// </summary>
    public static async Task ShowSerialPortAccessDeniedErrorAsync(string port, Exception ex)
    {
        var title = _localization?.GetString("connection.error.accessDenied") ?? "Access Denied to Serial Port";
        var message = string.Format(
            _localization?.GetString("connection.error.accessDeniedMessage") ?? "Cannot access serial port {0}. This could be caused by:",
            port);
        
        var okText = _localization?.GetString("messagebox.ok") ?? "OK";
        var analysisText = _localization?.GetString("connection.error.analysis") ?? "Error Analysis";
        
        var result = await ShowCustomAsync(title, message, MessageBoxIcon.Error, okText, analysisText);
        
        if (result == 1) // User clicked "Error Analysis"
        {
            await ShowSerialPortErrorAnalysisAsync(port);
        }
    }

    /// <summary>
    /// Show detailed error analysis window (problems only, no solutions)
    /// </summary>
    private static async Task ShowSerialPortErrorAnalysisAsync(string port)
    {
        var title = _localization?.GetString("connection.error.analysisTitle") ?? "Connection Error Analysis";
        var message = string.Format(
            _localization?.GetString("connection.error.accessDeniedMessage") ?? "Cannot access serial port {0}. This could be caused by:",
            port) + "\n\n" +
            (_localization?.GetString("connection.error.cause1") ?? "• Port is being used by another application") + "\n" +
            (_localization?.GetString("connection.error.cause2") ?? "• Previous connection was not properly closed") + "\n" +
            (_localization?.GetString("connection.error.cause3") ?? "• Insufficient permissions (especially for virtual serial ports)") + "\n" +
            (_localization?.GetString("connection.error.cause4") ?? "• Serial port device is locked or in an error state") + "\n" +
            (_localization?.GetString("connection.error.cause5") ?? "• For virtual serial ports: the creating process (e.g., socat) may have terminated");
        
        await ShowInfoAsync(title, message);
    }
    
    /// <summary>
    /// Show a message box with custom buttons
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="icon">Icon to display</param>
    /// <param name="buttons">Button labels (localized)</param>
    /// <returns>Index of clicked button, or -1 if dialog was closed</returns>
    public static async Task<int> ShowCustomAsync(string title, string message, MessageBoxIcon icon, params string[] buttons)
    {
        var dialog = CreateMessageBox(title, message, icon, buttons);
        var owner = GetMainWindow();
        
        if (owner != null)
        {
            var result = await dialog.ShowDialog<int?>(owner);
            return result ?? -1;
        }
        
        dialog.Show();
        return -1;
    }

    private static async Task ShowMessageAsync(string title, string message, MessageBoxIcon icon, string[] buttons)
    {
        var dialog = CreateMessageBox(title, message, icon, buttons);
        var owner = GetMainWindow();
        
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }

    private static Window CreateMessageBox(string title, string message, MessageBoxIcon icon, string[] buttons)
    {
        var dialog = new Window
        {
            Title = title,
            MinWidth = 400,
            MaxWidth = 600,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Transparent
        };

        var iconText = icon switch
        {
            MessageBoxIcon.Error => "❌",
            MessageBoxIcon.Warning => "⚠️",
            MessageBoxIcon.Info => "ℹ️",
            MessageBoxIcon.Question => "❓",
            _ => ""
        };

        var iconBlock = new TextBlock
        {
            Text = iconText,
            FontSize = 32,
            Margin = new Avalonia.Thickness(0, 0, 16, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 500,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var messagePanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Avalonia.Thickness(20),
            Children = { iconBlock, messageBlock }
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 10,
            Margin = new Avalonia.Thickness(20, 0, 20, 20)
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            var buttonIndex = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 100,
                Padding = new Avalonia.Thickness(16, 8)
            };

            button.Click += (s, e) => dialog.Close(buttonIndex);
            buttonPanel.Children.Add(button);
        }

        var mainPanel = new StackPanel
        {
            Children = { messagePanel, buttonPanel }
        };

        dialog.Content = mainPanel;
        return dialog;
    }
}

public enum MessageBoxIcon
{
    None,
    Info,
    Warning,
    Error,
    Question
}
