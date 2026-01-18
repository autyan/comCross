namespace ComCross.Platform.SharedMemory;

public sealed record SharedMemoryMapOptions(
    string Name,
    long CapacityBytes,
    string? UnixFilePath,
    bool UseFileBackedOnUnix,
    bool DeleteUnixFileOnDispose);
