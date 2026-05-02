using System.Runtime.InteropServices;

namespace ComCross.Core.Services;

public sealed class ComCrossPathService
{
    public ComCrossPathService()
        : this(AppContext.BaseDirectory, GetConfigDirectory(), GetLocalDataDirectory(), GetCacheDirectory())
    {
    }

    public ComCrossPathService(string installDirectory, string localDataDirectory)
        : this(installDirectory, localDataDirectory, localDataDirectory, Path.Combine(localDataDirectory, "cache"))
    {
    }

    public ComCrossPathService(
        string installDirectory,
        string configDirectory,
        string localDataDirectory,
        string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("Install directory cannot be empty.", nameof(installDirectory));
        }

        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            throw new ArgumentException("Config directory cannot be empty.", nameof(configDirectory));
        }

        if (string.IsNullOrWhiteSpace(localDataDirectory))
        {
            throw new ArgumentException("Local data directory cannot be empty.", nameof(localDataDirectory));
        }

        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("Cache directory cannot be empty.", nameof(cacheDirectory));
        }

        InstallDirectory = installDirectory;
        ConfigDirectory = configDirectory;
        LocalDataDirectory = localDataDirectory;
        CacheDirectory = cacheDirectory;
    }

    public string InstallDirectory { get; }

    public string ConfigDirectory { get; }

    public string LocalDataDirectory { get; }

    public string CacheDirectory { get; }

    public string BundledPluginsDirectory => Path.Combine(InstallDirectory, "bundled-plugins");

    public string RuntimePluginsDirectory => Path.Combine(LocalDataDirectory, "plugins");

    public string DatabaseDirectory => Path.Combine(LocalDataDirectory, "data");

    public string LogDirectory => Path.Combine(LocalDataDirectory, "logs");

    public string AppLogDirectory => Path.Combine(LogDirectory, "app");

    public string PluginHostLogDirectory => Path.Combine(LogDirectory, "plugin-host");

    public string ExportDirectory => Path.Combine(LocalDataDirectory, "exports");

    public string PluginSessionStorageDirectory => Path.Combine(LocalDataDirectory, "plugin-session-storage");

    private static string GetConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ComCross");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfigHome))
            {
                return Path.Combine(xdgConfigHome, "ComCross");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "ComCross");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross");
    }

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

    private static string GetCacheDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCacheHome))
            {
                return Path.Combine(xdgCacheHome, "ComCross");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".cache", "ComCross");
        }

        return Path.Combine(GetLocalDataDirectory(), "cache");
    }
}
