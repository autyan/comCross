using System.IO.MemoryMappedFiles;

namespace ComCross.Platform.SharedMemory;

public interface ISharedMemoryMapFactory
{
    SharedMemoryMapHandle Create(SharedMemoryMapOptions options);
}
