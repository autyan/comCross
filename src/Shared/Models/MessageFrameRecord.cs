namespace ComCross.Shared.Models;

/// <summary>
/// Versioned canonical frame fact for storage, export, analysis, and future decoder consumers.
/// </summary>
public interface IMessageFrameRecord
{
    int SchemaVersion { get; }
    long FrameId { get; }
    string SessionId { get; }
    DateTime TimestampUtc { get; }
    FrameDirection Direction { get; }
    byte[] RawData { get; }
    MessageFormat Format { get; }
    string Source { get; }
    IReadOnlyDictionary<string, string> Attributes { get; }
    int AttributeSchemaVersion { get; }
}

/// <summary>
/// Immutable versioned frame record. RawData is always the original payload bytes.
/// FrameId is unique only inside one session and starts at 1.
/// </summary>
public sealed record MessageFrameRecord(
    int SchemaVersion,
    long FrameId,
    string SessionId,
    DateTime TimestampUtc,
    FrameDirection Direction,
    byte[] RawData,
    MessageFormat Format,
    string Source,
    IReadOnlyDictionary<string, string> Attributes,
    int AttributeSchemaVersion = MessageFrameAttributes.SchemaVersion) : IMessageFrameRecord
{
    public const int CurrentSchemaVersion = 1;

    public MessageFrameRecord(
        long frameId,
        string sessionId,
        DateTime timestampUtc,
        FrameDirection direction,
        byte[] rawData,
        MessageFormat format,
        string source,
        IReadOnlyDictionary<string, string>? attributes = null,
        int attributeSchemaVersion = MessageFrameAttributes.SchemaVersion)
        : this(
            CurrentSchemaVersion,
            frameId,
            sessionId,
            timestampUtc,
            direction,
            rawData,
            format,
            source,
            attributes ?? MessageFrameAttributes.Empty,
            attributeSchemaVersion)
    {
    }
}
