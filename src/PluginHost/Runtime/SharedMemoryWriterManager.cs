using System.IO.MemoryMappedFiles;
using ComCross.Platform.SharedMemory;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.PluginHost.Runtime;

internal sealed class SharedMemoryWriterManager
{
    private readonly object _singleLock = new();
    private SwitchableSharedMemoryWriter? _switchableWriter;
    private IDisposable? _currentWriterHandle;

    private readonly object _multiLock = new();
    private readonly Dictionary<string, IDisposable> _writerHandlesBySession = new(StringComparer.Ordinal);

    public void Reset(object? pluginInstance)
    {
        Dictionary<string, IDisposable> handles;
        lock (_multiLock)
        {
            handles = new Dictionary<string, IDisposable>(_writerHandlesBySession, StringComparer.Ordinal);
            _writerHandlesBySession.Clear();
        }

        foreach (var kvp in handles)
        {
            try
            {
                if (pluginInstance is IMultiSessionDevicePlugin multi)
                {
                    multi.ClearSharedMemoryWriter(kvp.Key);
                }
            }
            catch
            {
            }

            try
            {
                kvp.Value.Dispose();
            }
            catch
            {
            }
        }

        lock (_singleLock)
        {
            _switchableWriter = null;
            try
            {
                _currentWriterHandle?.Dispose();
            }
            catch
            {
            }
            _currentWriterHandle = null;
        }
    }

    public bool TryApplyWriter(object pluginInstance, string sessionId, SharedMemorySegmentDescriptor descriptor)
    {
        if (pluginInstance is not IDevicePlugin plugin)
        {
            return false;
        }

        if (plugin is IMultiSessionDevicePlugin multi)
        {
            var nextWriter = CreateMappedWriter(sessionId, descriptor, out var handle);
            var nextHandle = new CompositeDisposable(nextWriter, handle);

            try
            {
                multi.SetSharedMemoryWriter(sessionId, nextWriter);
            }
            catch
            {
                try { nextHandle.Dispose(); } catch { }
                return false;
            }

            IDisposable? previous = null;
            lock (_multiLock)
            {
                if (_writerHandlesBySession.TryGetValue(sessionId, out var existing))
                {
                    previous = existing;
                }

                _writerHandlesBySession[sessionId] = nextHandle;
            }

            try { previous?.Dispose(); } catch { }
            return true;
        }

        lock (_singleLock)
        {
            var nextWriter = CreateMappedWriter(sessionId, descriptor, out var handle);

            if (_switchableWriter is null)
            {
                _switchableWriter = new SwitchableSharedMemoryWriter(nextWriter);
                plugin.SetSharedMemoryWriter(_switchableWriter);
                _currentWriterHandle = new CompositeDisposable(nextWriter, handle);
                return true;
            }

            var previous = _currentWriterHandle;
            _currentWriterHandle = new CompositeDisposable(nextWriter, handle);
            _switchableWriter.SwitchTo(nextWriter);

            try
            {
                previous?.Dispose();
            }
            catch
            {
            }

            return true;
        }
    }

    public void ClearWriterForSession(object? pluginInstance, string sessionId)
    {
        if (pluginInstance is IMultiSessionDevicePlugin multi)
        {
            try
            {
                multi.ClearSharedMemoryWriter(sessionId);
            }
            catch
            {
            }

            IDisposable? handle = null;
            lock (_multiLock)
            {
                if (_writerHandlesBySession.TryGetValue(sessionId, out var existing))
                {
                    handle = existing;
                    _writerHandlesBySession.Remove(sessionId);
                }
            }

            try
            {
                handle?.Dispose();
            }
            catch
            {
            }
        }
    }

    private static MappedSessionSegmentWriter CreateMappedWriter(string sessionId, SharedMemorySegmentDescriptor descriptor, out SharedMemoryMapHandle mapHandle)
    {
        var mapFactory = new SharedMemoryMapFactory();
        var useFileBackedOnUnix = !string.IsNullOrWhiteSpace(descriptor.UnixFilePath);
        mapHandle = mapFactory.Create(new SharedMemoryMapOptions(
            Name: descriptor.MapName,
            CapacityBytes: descriptor.MapCapacityBytes,
            UnixFilePath: descriptor.UnixFilePath,
            UseFileBackedOnUnix: useFileBackedOnUnix,
            DeleteUnixFileOnDispose: false));

        var accessor = mapHandle.Map.CreateViewAccessor(
            descriptor.SegmentOffset,
            descriptor.SegmentSize,
            MemoryMappedFileAccess.ReadWrite);

        var segment = new SessionSegment(sessionId, accessor, descriptor.SegmentSize, logger: null, initializeHeader: false);
        return new MappedSessionSegmentWriter(segment);
    }

    private sealed class MappedSessionSegmentWriter : ISharedMemoryWriter, IDisposable
    {
        private readonly SessionSegment _segment;

        public MappedSessionSegmentWriter(SessionSegment segment)
        {
            _segment = segment;
        }

        public bool TryWriteFrame(ReadOnlySpan<byte> data, out long frameId) => _segment.TryWriteFrame(data, out frameId);
        public long GetFreeSpace() => _segment.GetFreeSpace();
        public double GetUsageRatio() => _segment.GetUsageRatio();

        public void Dispose()
        {
            try
            {
                _segment.Dispose();
            }
            catch
            {
            }
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable _a;
        private readonly IDisposable _b;

        public CompositeDisposable(IDisposable a, IDisposable b)
        {
            _a = a;
            _b = b;
        }

        public void Dispose()
        {
            try { _a.Dispose(); } catch { }
            try { _b.Dispose(); } catch { }
        }
    }
}
