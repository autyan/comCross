using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComCross.Shared.Models;
using ComCross.Shell.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ComCross.Shell.Views;

public partial class MainWindow : Window
{
    private bool _autoScreenshotScheduled;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.CloseSessionDetail();
            vm.IsSettingsOpen = false;
            vm.IsNotificationsOpen = false;
            return;
        }

        if (ShouldIgnoreHotkey(e.Source))
        {
            return;
        }

        if (IsModifierKey(e.Key))
        {
            return;
        }

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        if (await vm.CommandCenter.TryExecuteHotkeyAsync(gesture.ToString()))
        {
            e.Handled = true;
        }
    }

    private static bool ShouldIgnoreHotkey(object? source)
    {
        return source is TextBox or ComboBox or AutoCompleteBox;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _ = ApplyStartupSurfaceAsync();

        if (_autoScreenshotScheduled)
        {
            return;
        }

        var outputPath = Environment.GetEnvironmentVariable("COMCROSS_SHELL_AUTO_SCREENSHOT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        _autoScreenshotScheduled = true;
        _ = CaptureScreenshotAsync(outputPath);
    }

    private async Task ApplyStartupSurfaceAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var startupSurface = Environment.GetEnvironmentVariable("COMCROSS_SHELL_START_SURFACE");
        if (string.IsNullOrWhiteSpace(startupSurface))
        {
            return;
        }

        await Task.Delay(350);

        switch (startupSurface.Trim().ToLowerInvariant())
        {
            case "settings":
                await Dispatcher.UIThread.InvokeAsync(vm.OpenSettings);
                break;
            case "notifications":
                await Dispatcher.UIThread.InvokeAsync(vm.OpenNotifications);
                break;
            case "commands":
                await Dispatcher.UIThread.InvokeAsync(() => vm.RightToolDock.OpenCommandEditor());
                break;
            case "metrics":
                await Dispatcher.UIThread.InvokeAsync(() => vm.MessageStream.IsMetricsBarVisible = true);
                break;
            case "session-detail":
                await OpenSessionDetailWhenReadyAsync(vm);
                break;
        }
    }

    private static async Task OpenSessionDetailWhenReadyAsync(MainWindowViewModel vm)
    {
        for (var i = 0; i < 20; i++)
        {
            Session? activeSession = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                activeSession = vm.ActiveSession;
            });

            if (activeSession is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => vm.OpenSessionDetail(activeSession));
                return;
            }

            await Task.Delay(200);
        }
    }

    private async Task CaptureScreenshotAsync(string outputPath)
    {
        try
        {
            var delayMs = 1200;
            if (int.TryParse(Environment.GetEnvironmentVariable("COMCROSS_SHELL_AUTO_SCREENSHOT_DELAY_MS"), out var parsedDelay)
                && parsedDelay > 0)
            {
                delayMs = parsedDelay;
            }

            await Task.Delay(delayMs);

            var width = Math.Max(1, (int)Math.Ceiling(ClientSize.Width));
            var height = Math.Max(1, (int)Math.Ceiling(ClientSize.Height));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            bitmap.Render(this);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(outputPath);
            bitmap.Save(stream);

            if (string.Equals(Environment.GetEnvironmentVariable("COMCROSS_SHELL_AUTO_EXIT_AFTER_SCREENSHOT"), "1", StringComparison.Ordinal))
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Shell] Auto screenshot failed: {ex}");
        }
    }
}
