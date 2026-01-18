using ComCross.PluginSdk;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Optional capabilities for device connections that can write incoming frames to shared memory
/// and accept host-provided backpressure signals.
/// </summary>
public interface ISharedMemoryCapableConnection
{
    void SetSharedMemorySegment(ISharedMemoryWriter segment);

    void SetBackpressureLevel(BackpressureLevel level);
}
