using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class ArchivingFrameStore : IFrameStore
{
    private readonly IFrameStore _inner;
    private readonly SessionArchiveWriter _archiveWriter;

    public ArchivingFrameStore(IFrameStore inner, SessionArchiveWriter archiveWriter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _archiveWriter = archiveWriter ?? throw new ArgumentNullException(nameof(archiveWriter));
        _inner.FramesAppended += sessionId => FramesAppended?.Invoke(sessionId);
    }

    public event Action<string>? FramesAppended;

    public long Append(
        string sessionId,
        DateTime timestampUtc,
        FrameDirection direction,
        byte[] rawData,
        MessageFormat format,
        string source,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        rawData ??= Array.Empty<byte>();
        source ??= string.Empty;

        var frameId = _inner.Append(sessionId, timestampUtc, direction, rawData, format, source, attributes);
        var normalizedAttributes = MessageFrameAttributes.Normalize(attributes);
        _archiveWriter.EnqueueIfEnabled(new MessageFrameRecord(
            frameId,
            sessionId,
            timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime(),
            direction,
            rawData,
            format,
            source,
            normalizedAttributes));
        return frameId;
    }

    public IReadOnlyList<FrameRecord> ReadAfter(string sessionId, long afterFrameId, int maxCount, out long firstAvailableFrameId)
        => _inner.ReadAfter(sessionId, afterFrameId, maxCount, out firstAvailableFrameId);

    public (long FirstAvailableFrameId, long LastFrameId, long DroppedFrames) GetWindowInfo(string sessionId)
        => _inner.GetWindowInfo(sessionId);

    public void Clear(string sessionId)
        => _inner.Clear(sessionId);
}
