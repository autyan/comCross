using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;

namespace ComCross.Shell;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;
    private bool _isShuttingDown = false;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Capture global unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }
    
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        System.Diagnostics.Debug.WriteLine($"[FATAL] Unhandled exception: {ex?.Message}");
        System.Diagnostics.Debug.WriteLine($"[FATAL] Stack trace: {ex?.StackTrace}");
        if (ex?.InnerException != null)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] Inner exception: {ex.InnerException.Message}");
        }
    }
    
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[ERROR] Unobserved task exception: {e.Exception?.Message}");
        System.Diagnostics.Debug.WriteLine($"[ERROR] Stack trace: {e.Exception?.StackTrace}");
        e.SetObserved(); // Prevent application crash
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _viewModel
            };
            
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        
        // Cancel the first shutdown to run async cleanup
        e.Cancel = true;
        
        // Run cleanup asynchronously with timeout protection
        Task.Run(async () =>
        {
            try
            {
                // Clean up resources with 5 second timeout
                if (_viewModel != null)
                {
                    var cleanupTask = _viewModel.CleanupWithProgressAsync();
                    var timeoutTask = Task.Delay(5000);
                    await Task.WhenAny(cleanupTask, timeoutTask);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
            finally
            {
                // Always shutdown on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                });
            }
        });
    }
}
