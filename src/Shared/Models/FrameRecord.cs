namespace ComCross.Shared.Models;

/// <summary>
/// Legacy in-memory fact record for RX/TX physical frames.
/// RawData is always the original payload bytes (no hex/text conversion).
/// </summary>
public sealed record FrameRecord(
    long FrameId,
    string SessionId,
    DateTime TimestampUtc,
    FrameDirection Direction,
    byte[] RawData,
    MessageFormat Format,
    string Source,
    IReadOnlyDictionary<string, string> Attributes,
    int AttributeSchemaVersion = MessageFrameAttributes.SchemaVersion)
{
    public MessageFrameRecord ToMessageFrameRecord()
        => new(
            FrameId,
            SessionId,
            TimestampUtc,
            Direction,
            RawData,
            Format,
            Source,
            Attributes,
            AttributeSchemaVersion);
}
