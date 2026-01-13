namespace ComCross.Shared.Models;

/// <summary>
/// Represents a device (serial port)
/// </summary>
public sealed class Device
{
    public required string Port { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Manufacturer { get; init; }
    public string? SerialNumber { get; init; }
    public bool IsFavorite { get; set; }
}
