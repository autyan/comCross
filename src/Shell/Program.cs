using Avalonia;
using ComCross.Core.Application;
using ComCross.Core.Services;
using ComCross.Platform.SingleInstance;
using ComCross.Platform.UserDirectories;
using System;

namespace ComCross.Shell;

class Program
{
    private static IDisposable? _instanceLock;

    [STAThread]
    public static int Main(string[] args)
    {
        var instance = ComCrossInstanceResolver.Resolve(AppContext.BaseDirectory, args);
        var userDirectories = new PlatformUserDirectoryProvider();
        var paths = new ComCrossPathService(userDirectories, instance);

        var singleInstance = new PlatformSingleInstanceLock();
        _instanceLock = singleInstance.TryAcquire(instance.InstanceId, paths.LocalDataDirectory, out var lockError);
        if (_instanceLock is null)
        {
            Console.Error.WriteLine(lockError ?? "ComCross is already running for this instance.");
            return 1;
        }

        ShellStartupContext.Instance = instance;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal static class ShellStartupContext
{
    public static ComCrossInstanceIdentity Instance { get; set; } = ComCrossInstanceIdentity.Stable();
}
