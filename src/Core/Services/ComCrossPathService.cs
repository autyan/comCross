using System.Runtime.InteropServices;

namespace ComCross.Core.Services;

public sealed class ComCrossPathService
{
    public ComCrossPathService()
        : this(AppContext.BaseDirectory, GetLocalDataDirectory())
    {
    }

    public ComCrossPathService(string installDirectory, string localDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("Install directory cannot be empty.", nameof(installDirectory));
        }

        if (string.IsNullOrWhiteSpace(localDataDirectory))
        {
            throw new ArgumentException("Local data directory cannot be empty.", nameof(localDataDirectory));
        }

        InstallDirectory = installDirectory;
        LocalDataDirectory = localDataDirectory;
    }

    public string InstallDirectory { get; }

    public string LocalDataDirectory { get; }

    public string BundledPluginsDirectory => Path.Combine(InstallDirectory, "bundled-plugins");

    public string RuntimePluginsDirectory => Path.Combine(LocalDataDirectory, "plugins");

    private static string GetLocalDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ComCross");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
            {
                return Path.Combine(xdgDataHome, "ComCross");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "ComCross");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComCross");
    }
}
