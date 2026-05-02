namespace ComCross.Platform.UserDirectories;

public interface IPlatformUserDirectoryProvider
{
    string ConfigHome { get; }

    string LocalDataHome { get; }

    string CacheHome { get; }
}
