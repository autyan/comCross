namespace ComCross.Shared.Models;

public sealed class NotificationItem
{
    public required string Id { get; init; }
    public NotificationCategory Category { get; init; } = NotificationCategory.System;
    public required string MessageKey { get; init; }
    public string MessageArgsJson { get; init; } = "[]";
    public NotificationLevel Level { get; init; } = NotificationLevel.Info;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}

public enum NotificationLevel
{
    Info,
    Warning,
    Error
}

public enum NotificationCategory
{
    System,
    Storage,
    Connection,
    Export
}
