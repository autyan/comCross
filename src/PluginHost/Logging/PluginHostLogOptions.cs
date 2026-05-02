namespace ComCross.PluginHost.Logging;

public sealed record PluginHostLogOptions(
    string Directory,
    string Format,
    string MinLevel,
    string FileKey,
    long ArchiveAboveBytes,
    int RetentionDays);
