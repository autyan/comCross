using Avalonia;

namespace ComCross.Startup;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupContext.Args = args;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
