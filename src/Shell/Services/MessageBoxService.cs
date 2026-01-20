using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Threading.Tasks;
using ComCross.Shared.Services;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using Microsoft.Extensions.DependencyInjection;

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
        var dialogFactory = App.ServiceProvider.GetRequiredService<IMessageBoxDialogFactory>();
        var owner = GetMainWindow();
        
        if (owner != null)
        {
            return await dialogFactory.ShowCustomAsync(owner, title, message, icon, buttons);
        }
        return -1;
    }

    private static async Task ShowMessageAsync(string title, string message, MessageBoxIcon icon, string[] buttons)
    {
        var dialogFactory = App.ServiceProvider.GetRequiredService<IMessageBoxDialogFactory>();
        var owner = GetMainWindow();
        
        if (owner != null)
        {
            await dialogFactory.ShowMessageAsync(owner, title, message, icon, buttons);
        }
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
