using ComCross.Platform.UserDirectories;

namespace ComCross.Startup;

internal sealed record StartupPaths(string LocalDataDirectory, string StartupLogDirectory)
{
    public static StartupPaths Create(
        StartupInstanceIdentity instance,
        IPlatformUserDirectoryProvider userDirectories)
    {
        var localData = Path.Combine(userDirectories.LocalDataHome, instance.DirectoryName);
        return new StartupPaths(localData, Path.Combine(localData, "logs", "startup"));
    }
}
