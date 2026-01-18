using System.Threading;

namespace ComCross.PluginSdk;

/// <summary>
/// Default implementation for "writer switching".
///
/// A plugin can keep a stable reference to this wrapper, while the host (or the plugin itself)
/// can switch the underlying writer when a larger segment is granted.
/// </summary>
public sealed class SwitchableSharedMemoryWriter : ISharedMemoryWriter
{
    private ISharedMemoryWriter _current;

    public SwitchableSharedMemoryWriter(ISharedMemoryWriter initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    /// <summary>
    /// Switch to a new underlying writer (e.g., after segment upgrade).
    /// </summary>
    public void SwitchTo(ISharedMemoryWriter next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Interlocked.Exchange(ref _current, next);
    }

    public bool TryWriteFrame(ReadOnlySpan<byte> data, out long frameId)
        => _current.TryWriteFrame(data, out frameId);

    public long GetFreeSpace() => _current.GetFreeSpace();

    public double GetUsageRatio() => _current.GetUsageRatio();
}
