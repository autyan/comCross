namespace ComCross.Shared.Models;

/// <summary>
/// In-memory fact record for RX/TX physical frames.
/// RawData is always the original payload bytes (no hex/text conversion).
/// </summary>
public sealed record FrameRecord(
    long FrameId,
    string SessionId,
    DateTime TimestampUtc,
    FrameDirection Direction,
    byte[] RawData,
    MessageFormat Format,
    string Source);
