namespace ComCross.Platform.UserDirectories;

public sealed class PlatformUserDirectoryProvider : IPlatformUserDirectoryProvider
{
    public string ConfigHome => ResolveXdgHome("XDG_CONFIG_HOME", ".config");

    public string LocalDataHome => ResolveXdgHome("XDG_DATA_HOME", Path.Combine(".local", "share"));

    public string CacheHome => ResolveXdgHome("XDG_CACHE_HOME", ".cache");

    private static string ResolveXdgHome(string variableName, string fallbackRelativePath)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, fallbackRelativePath);
    }
}
