using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

public interface IFrameStore
{
    event Action<string>? FramesAppended;

    /// <summary>
    /// Appends a frame into the per-session window and returns the assigned frame id.
    /// </summary>
    long Append(string sessionId, DateTime timestampUtc, FrameDirection direction, byte[] rawData, MessageFormat format, string source);

    /// <summary>
    /// Reads frames strictly after <paramref name="afterFrameId"/>.
    /// </summary>
    IReadOnlyList<FrameRecord> ReadAfter(string sessionId, long afterFrameId, int maxCount, out long firstAvailableFrameId);

    (long FirstAvailableFrameId, long LastFrameId, long DroppedFrames) GetWindowInfo(string sessionId);

    void Clear(string sessionId);
}
