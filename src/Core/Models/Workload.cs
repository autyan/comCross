namespace ComCross.Core.Models;

/// <summary>
/// Workload represents a logical grouping of communication sessions.
/// </summary>
public sealed class Workload
{
    /// <summary>
    /// Unique identifier for the workload.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Display name of the workload.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates if this is the default workload.
    /// The default workload is automatically created and cannot be deleted.
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last modification timestamp (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Optional description of the workload.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Sessions contained in this workload.
    /// </summary>
    public List<string> SessionIds { get; set; } = new();
    
    /// <summary>
    /// Create a new workload with the specified name.
    /// </summary>
    /// <param name="name">Workload name</param>
    /// <param name="isDefault">Whether this is the default workload</param>
    /// <returns>New workload instance</returns>
    public static Workload Create(string name, bool isDefault = false)
    {
        return new Workload
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            IsDefault = isDefault,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Add a session to this workload.
    /// </summary>
    /// <param name="sessionId">Session ID to add</param>
    public void AddSession(string sessionId)
    {
        if (!SessionIds.Contains(sessionId))
        {
            SessionIds.Add(sessionId);
            UpdatedAt = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Remove a session from this workload.
    /// </summary>
    /// <param name="sessionId">Session ID to remove</param>
    /// <returns>True if session was removed, false if not found</returns>
    public bool RemoveSession(string sessionId)
    {
        if (SessionIds.Remove(sessionId))
        {
            UpdatedAt = DateTime.UtcNow;
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Update the workload name.
    /// </summary>
    /// <param name="newName">New name</param>
    public void Rename(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }
}
