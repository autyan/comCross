namespace ComCross.Platform.UserDirectories;

public sealed class PlatformUserDirectoryProvider : IPlatformUserDirectoryProvider
{
    public string ConfigHome => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public string LocalDataHome => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string CacheHome => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
