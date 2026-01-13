namespace ComCross.Shared.Models;

/// <summary>
/// Represents a device connection session
/// </summary>
public sealed class Session
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Port { get; init; }
    public required int BaudRate { get; init; }
    public SessionStatus Status { get; set; }
    public DateTime? StartTime { get; set; }
    public long RxBytes { get; set; }
    public long TxBytes { get; set; }
    public SerialSettings Settings { get; set; } = new();
}

public enum SessionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
