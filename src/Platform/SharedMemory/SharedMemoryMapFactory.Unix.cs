using System.IO.MemoryMappedFiles;

namespace ComCross.Platform.SharedMemory;

public sealed class SharedMemoryMapFactory : ISharedMemoryMapFactory
{
    public SharedMemoryMapHandle Create(SharedMemoryMapOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));
        if (options.CapacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options), "CapacityBytes must be > 0.");

        if (!options.UseFileBackedOnUnix)
        {
            var anonymous = MemoryMappedFile.CreateNew(null, options.CapacityBytes, MemoryMappedFileAccess.ReadWrite);
            var anonymousDescriptor = new SharedMemoryMapDescriptor(options.Name, options.CapacityBytes, unixFilePath: null, deleteUnixFileOnDispose: false);
            return new SharedMemoryMapHandle(anonymous, anonymousDescriptor);
        }

        var resolvedPath = !string.IsNullOrWhiteSpace(options.UnixFilePath)
            ? options.UnixFilePath!
            : GetDefaultUnixBackingFilePath(options.Name);

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var stream = new FileStream(
                   resolvedPath,
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.ReadWrite))
        {
            stream.SetLength(options.CapacityBytes);
            var map = MemoryMappedFile.CreateFromFile(
                stream,
                mapName: null,
                capacity: options.CapacityBytes,
                access: MemoryMappedFileAccess.ReadWrite,
                inheritability: HandleInheritability.None,
                leaveOpen: false);

            var descriptor = new SharedMemoryMapDescriptor(options.Name, options.CapacityBytes, resolvedPath, options.DeleteUnixFileOnDispose);
            return new SharedMemoryMapHandle(map, descriptor);
        }
    }

    private static string GetDefaultUnixBackingFilePath(string name)
    {
        var safeName = name
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');

        return Path.Combine(Path.GetTempPath(), "comcross", $"{safeName}.mmf");
    }
}
