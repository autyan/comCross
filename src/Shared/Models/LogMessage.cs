namespace ComCross.Shared.Models;

/// <summary>
/// Represents a single log message in the message stream
/// </summary>
public sealed class LogMessage
{
    public required string Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Content { get; init; }
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string? Source { get; init; }
    public byte[]? RawData { get; init; }
    public MessageFormat Format { get; init; } = MessageFormat.Text;
}

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public enum MessageFormat
{
    Text,
    Hex
}
