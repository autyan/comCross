using ComCross.Core.Application;
using ComCross.Platform.UserDirectories;

namespace ComCross.Core.Services;

public sealed class ComCrossPathService
{
    public ComCrossPathService()
        : this(new PlatformUserDirectoryProvider(), ComCrossInstanceIdentity.Stable(), AppContext.BaseDirectory)
    {
    }

    public ComCrossPathService(string installDirectory, string localDataDirectory)
        : this(installDirectory, localDataDirectory, localDataDirectory, Path.Combine(localDataDirectory, "cache"))
    {
    }

    public ComCrossPathService(
        IPlatformUserDirectoryProvider userDirectories,
        ComCrossInstanceIdentity instance,
        string? installDirectory = null)
        : this(
            installDirectory ?? AppContext.BaseDirectory,
            Path.Combine(userDirectories.ConfigHome, instance.DirectoryName),
            Path.Combine(userDirectories.LocalDataHome, instance.DirectoryName),
            Path.Combine(userDirectories.CacheHome, instance.DirectoryName),
            instance)
    {
    }

    public ComCrossPathService(
        string installDirectory,
        string configDirectory,
        string localDataDirectory,
        string cacheDirectory)
        : this(installDirectory, configDirectory, localDataDirectory, cacheDirectory, ComCrossInstanceIdentity.Stable())
    {
    }

    private ComCrossPathService(
        string installDirectory,
        string configDirectory,
        string localDataDirectory,
        string cacheDirectory,
        ComCrossInstanceIdentity instance)
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
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public ComCrossInstanceIdentity Instance { get; }

    public string InstallDirectory { get; }

    public string ConfigDirectory { get; }

    public string LocalDataDirectory { get; }

    public string CacheDirectory { get; }

    public string BundledPluginsDirectory => Path.Combine(InstallDirectory, "bundled-plugins");

    public string RuntimePluginsDirectory => Path.Combine(LocalDataDirectory, "plugins");

    public string DatabaseDirectory => Path.Combine(LocalDataDirectory, "data");

    public string LogDirectory => Path.Combine(LocalDataDirectory, "logs");

    public string AppLogDirectory => Path.Combine(LogDirectory, "app");

    public string StartupLogDirectory => Path.Combine(LogDirectory, "startup");

    public string PluginHostLogDirectory => Path.Combine(LogDirectory, "plugin-host");

    public string ExportDirectory => Path.Combine(LocalDataDirectory, "exports");

    public string PluginSessionStorageDirectory => Path.Combine(LocalDataDirectory, "plugin-session-storage");
}
