using System.IO.MemoryMappedFiles;

namespace ComCross.Platform.SharedMemory;

public sealed class SharedMemoryMapFactory : ISharedMemoryMapFactory
{
    public SharedMemoryMapHandle Create(SharedMemoryMapOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));
        if (options.CapacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options), "CapacityBytes must be > 0.");

        var map = MemoryMappedFile.CreateOrOpen(options.Name, options.CapacityBytes, MemoryMappedFileAccess.ReadWrite);
        var descriptor = new SharedMemoryMapDescriptor(options.Name, options.CapacityBytes, unixFilePath: null, deleteUnixFileOnDispose: false);
        return new SharedMemoryMapHandle(map, descriptor);
    }
}
