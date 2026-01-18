using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Core.Services;

/// <summary>
/// Manages lifecycle of per-session shared memory segments on the main process side.
///
/// Responsibilities:
/// - Ensure shared memory initialized
/// - Allocate/replace a session segment
/// - Start/stop reader loops
/// - Provide cross-process descriptor for PluginHost to reopen mappings
/// </summary>
public sealed class SharedMemorySessionService
{
    private readonly SharedMemoryManager _sharedMemoryManager;
    private readonly SharedMemoryReader _sharedMemoryReader;

    public SharedMemorySessionService(SharedMemoryManager sharedMemoryManager, SharedMemoryReader sharedMemoryReader)
    {
        _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
        _sharedMemoryReader = sharedMemoryReader ?? throw new ArgumentNullException(nameof(sharedMemoryReader));
    }

    public async Task CleanupAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            await _sharedMemoryReader.StopReadingAsync(sessionId);
        }
        catch
        {
        }

        try
        {
            _sharedMemoryManager.ReleaseSegment(sessionId);
        }
        catch
        {
        }
    }

    public void StartReading(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var segment = _sharedMemoryManager.GetSegment(sessionId);
        if (segment is null)
        {
            return;
        }

        _sharedMemoryReader.StartReading(sessionId, segment);
    }

    public async Task ReleaseSegmentAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            await _sharedMemoryReader.StopReadingAsync(sessionId);
        }
        catch
        {
        }

        try
        {
            _sharedMemoryManager.ReleaseSegment(sessionId);
        }
        catch
        {
        }
    }

    public async Task<SharedMemorySegmentDescriptor> AllocateOrReplaceAsync(string sessionId, int requestedBytes)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Missing sessionId.", nameof(sessionId));
        }

        if (requestedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedBytes), "requestedBytes must be > 0.");
        }

        _sharedMemoryManager.EnsureInitialized();

        // Best-effort: stop existing reader loop before replacing the segment.
        var existing = _sharedMemoryManager.GetSegment(sessionId);
        if (existing is not null)
        {
            await CleanupAsync(sessionId);
        }

        var segment = await _sharedMemoryManager.AllocateSegmentAsync(sessionId, requestedBytes);

        // Note: do NOT start reading until PluginHost successfully applies the descriptor,
        // so we don't spin reader loops on a segment that will be rolled back.
        _ = segment;

        if (!_sharedMemoryManager.TryGetSegmentDescriptor(sessionId, out var descriptor))
        {
            await ReleaseSegmentAsync(sessionId);
            throw new InvalidOperationException("Failed to resolve segment descriptor.");
        }

        return descriptor;
    }
}
