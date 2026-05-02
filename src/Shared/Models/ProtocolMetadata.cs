namespace ComCross.Shared.Models;

/// <summary>
/// Metadata information about a protocol.
/// Used for protocol discovery and display.
/// </summary>
public sealed class ProtocolMetadata
{
    /// <summary>
    /// Unique identifier for this protocol.
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Protocol version.
    /// </summary>
    public string Version { get; init; } = string.Empty;
    
    /// <summary>
    /// Brief description.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Protocol category (e.g., "Industrial", "IoT", "Custom").
    /// </summary>
    public string Category { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether this is a built-in protocol.
    /// </summary>
    public bool IsBuiltIn { get; init; }
    
    /// <summary>
    /// Icon name for UI display (optional).
    /// </summary>
    public string? IconName { get; init; }
    
    /// <summary>
    /// Author or organization (optional).
    /// </summary>
    public string? Author { get; init; }
    
    /// <summary>
    /// Documentation URL (optional).
    /// </summary>
    public string? DocumentationUrl { get; init; }
    
    /// <summary>
    /// When this protocol was registered.
    /// </summary>
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Create metadata from a parser instance.
    /// </summary>
    public static ProtocolMetadata FromParser(ComCross.Shared.Interfaces.IMessageParser parser)
    {
        return new ProtocolMetadata
        {
            Id = parser.Id,
            Name = parser.Name,
            Version = parser.Version,
            Description = parser.Description,
            Category = parser.Category,
            IsBuiltIn = parser.IsBuiltIn,
            RegisteredAt = DateTime.UtcNow
        };
    }
}
