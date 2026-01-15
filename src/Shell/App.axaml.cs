using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using System.Threading.Tasks;

namespace ComCross.Shell;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;
    private bool _isShuttingDown = false;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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
            
            // Handle application shutdown
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
        
        // Cancel the shutdown to run async cleanup
        e.Cancel = true;
        
        // Run cleanup asynchronously on background thread
        Task.Run(async () =>
        {
            try
            {
                // Clean up resources with progress dialog
                if (_viewModel != null)
                {
                    await _viewModel.CleanupWithProgressAsync();
                }
            }
            finally
            {
                // Force shutdown on UI thread after cleanup
                Dispatcher.UIThread.Post(() =>
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
