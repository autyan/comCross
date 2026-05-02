using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ComCross.Startup;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var launcher = new StartupLauncher(StartupContext.Args);
            var result = await launcher.RunAsync();
            if (result.Ok)
            {
                desktop.Shutdown();
                return;
            }

            var window = new StartupErrorWindow(result.Title, result.Message, result.LogDirectory);
            window.Closed += (_, _) => desktop.Shutdown(1);
            desktop.MainWindow = window;
            window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
