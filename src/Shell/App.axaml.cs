using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using ComCross.Shell.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell;

public partial class App : Application
{
    private static IServiceProvider? _serviceProvider;
    private Core.Application.IAppHost? _appHost;
    private MainWindowViewModel? _viewModel;
    private bool _isShuttingDown = false;
    
    /// <summary>
    /// Global service provider for accessing DI services.
    /// </summary>
    public static IServiceProvider ServiceProvider => _serviceProvider ?? throw new InvalidOperationException("App not initialized");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // 1. Configure DI container
        var services = new ServiceCollection();
        services.AddComCrossShell(); 
        _serviceProvider = services.BuildServiceProvider();

        // 2. Get the AppHost from DI
        _appHost = _serviceProvider.GetRequiredService<Core.Application.IAppHost>();
        
        // 3. Setup Global Exception Handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            Console.Error.WriteLine($"UNHANDLED EXCEPTION: {e.ExceptionObject}");
        }
        catch
        {
            // Best-effort logging only.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Console.Error.WriteLine($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
            e.SetObserved();
        }
        catch
        {
            // Best-effort logging only.
        }
    }
    
    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try 
            {
                Console.Error.WriteLine("[Shell] Framework initialization started");
                // 4. Initialize Core Engine (Database, Configuration, Plugins)
                if (_appHost != null)
                {
                    Console.Error.WriteLine("[Shell] Initializing Core Engine...");
                    await _appHost.InitializeAsync();
                    Console.Error.WriteLine("[Shell] Core Engine initialized");
                }

                // Initialize static services
                var localization = _serviceProvider!.GetRequiredService<Shared.Services.ILocalizationService>();
                Shell.Services.MessageBoxService.Initialize(localization);
                Console.Error.WriteLine("[Shell] Services initialized");

                // 5. Build UI
                _viewModel = _serviceProvider!.GetRequiredService<MainWindowViewModel>();
                var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
                mainWindow.DataContext = _viewModel;
                desktop.MainWindow = mainWindow;
                // Defensive: explicitly show the window, but do it asynchronously.
                // Calling Show()/Activate() synchronously here can race the dispatcher startup on Wayland.
                mainWindow.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        desktop.MainWindow?.Show();
                        desktop.MainWindow?.Activate();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Shell] Failed to show MainWindow: {ex}");
                    }
                });
                Console.Error.WriteLine("[Shell] MainWindow created");
                
                desktop.ShutdownRequested += OnShutdownRequested;
            }
            catch (Exception ex)
            {
                // Fallback for fatal startup errors
                Console.Error.WriteLine($"FATAL STARTUP ERROR: {ex}");
            }
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
                // 1. UI Cleanup
                if (_viewModel != null)
                {
                    var cleanupTask = _viewModel.CleanupWithProgressAsync();
                    var timeoutTask = Task.Delay(2000);
                    await Task.WhenAny(cleanupTask, timeoutTask);
                }

                // 2. Core Cleanup (Stop Plugins, Drivers, DB)
                if (_appHost != null)
                {
                    await _appHost.ShutdownAsync();
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
