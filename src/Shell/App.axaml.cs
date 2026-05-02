using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Services;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using ComCross.Shell.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell;

public partial class App : Application
{
    private static IServiceProvider? _serviceProvider;
    private IServiceScope? _mainWindowScope;
    private Core.Application.IAppHost? _appHost;
    private MainWindowViewModel? _viewModel;
    private bool _isShuttingDown;
    private bool _shutdownReady;
    
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
                var messageBoxDialogFactory = _serviceProvider!.GetRequiredService<IMessageBoxDialogFactory>();
                Shell.Services.MessageBoxService.Initialize(localization, messageBoxDialogFactory);
                Shell.Services.LocalizationManager.Initialize(localization);

                var connectDialogService = _serviceProvider!.GetRequiredService<IConnectDialogService>();
                var sessionRenameDialogService = _serviceProvider!.GetRequiredService<ISessionRenameDialogService>();
                var testConnectDialogService = _serviceProvider!.GetRequiredService<ITestConnectDialogService>();
                Shell.Services.ShellUiServices.Initialize(connectDialogService, sessionRenameDialogService, testConnectDialogService);
                Console.Error.WriteLine("[Shell] Services initialized");

                // 5. Build UI
                _mainWindowScope = _serviceProvider!.CreateScope();

                _viewModel = _mainWindowScope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
                var mainWindow = _mainWindowScope.ServiceProvider.GetRequiredService<MainWindow>();
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
        if (_shutdownReady)
        {
            return;
        }

        e.Cancel = true;

        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        _ = ShutdownApplicationAsync();
    }

    private async Task ShutdownApplicationAsync()
    {
        try
        {
            var isHeadlessRegressionShutdown =
                string.Equals(Environment.GetEnvironmentVariable("COMCROSS_SHELL_AUTO_EXIT_AFTER_SCREENSHOT"), "1", StringComparison.Ordinal);

            // 1. UI Cleanup
            if (_viewModel != null)
            {
                var cleanupTask = isHeadlessRegressionShutdown
                    ? _viewModel.CleanupAsync(showProgress: false)
                    : _viewModel.CleanupWithProgressAsync();
                // i18n-ignore
                await RunWithTimeoutBestEffortAsync(cleanupTask, TimeSpan.FromSeconds(2), "UI cleanup");
            }

            // 2. Core Cleanup (Stop Plugins, Drivers, DB)
            if (_appHost != null)
            {
                // i18n-ignore
                await RunWithTimeoutBestEffortAsync(_appHost.ShutdownAsync(), TimeSpan.FromSeconds(12), "Core cleanup");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
        }
        finally
        {
            await DisposeMainWindowScopeBestEffortAsync();

            // Always shutdown on UI thread. Mark ready first so the second ShutdownRequested pass is allowed through.
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _shutdownReady = true;
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Final UI shutdown error: {ex.Message}");
            }
        }
    }

    private static async Task RunWithTimeoutBestEffortAsync(Task task, TimeSpan timeout, string operation)
    {
        try
        {
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == task)
            {
                await task;
                return;
            }

            System.Diagnostics.Debug.WriteLine($"{operation} timed out after {timeout.TotalSeconds:0.#}s.");
            _ = task.ContinueWith(
                static t =>
                {
                    _ = t.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{operation} error: {ex.Message}");
        }
    }

    private async Task DisposeMainWindowScopeBestEffortAsync()
    {
        try
        {
            if (_mainWindowScope is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                _mainWindowScope?.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Main window scope dispose error: {ex.Message}");
        }
        finally
        {
            _mainWindowScope = null;
        }
    }
}
