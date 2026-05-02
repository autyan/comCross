using System.IO.MemoryMappedFiles;

namespace ComCross.Platform.SharedMemory;

public sealed class SharedMemoryMapHandle : IDisposable
{
    public SharedMemoryMapHandle(MemoryMappedFile map, SharedMemoryMapDescriptor descriptor)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public MemoryMappedFile Map { get; }

    public SharedMemoryMapDescriptor Descriptor { get; }

    public void Dispose()
    {
        try
        {
            Map.Dispose();
        }
        finally
        {
            Descriptor.TryCleanup();
        }
    }
}

public sealed class SharedMemoryMapDescriptor
{
    public SharedMemoryMapDescriptor(string name, long capacityBytes, string? unixFilePath, bool deleteUnixFileOnDispose)
    {
        Name = name;
        CapacityBytes = capacityBytes;
        UnixFilePath = unixFilePath;
        DeleteUnixFileOnDispose = deleteUnixFileOnDispose;
    }

    public string Name { get; }
    public long CapacityBytes { get; }
    public string? UnixFilePath { get; }
    public bool DeleteUnixFileOnDispose { get; }

    internal void TryCleanup()
    {
        if (!DeleteUnixFileOnDispose)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(UnixFilePath))
        {
            return;
        }

        try
        {
            File.Delete(UnixFilePath);
        }
        catch
        {
        }
    }
}
