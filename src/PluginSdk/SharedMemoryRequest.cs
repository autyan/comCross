namespace ComCross.PluginSdk;

/// <summary>
/// Shared memory request model for a session/capability.
/// Plugins can request a segment size within a bounded range.
/// The host (main process) decides the actual allocation size and whether growth is allowed.
/// </summary>
public sealed record SharedMemoryRequest
{
    /// <summary>
    /// Minimum acceptable segment size in bytes.
    /// Host may reject the connection if it cannot allocate at least this size.
    /// </summary>
    public required int MinBytes { get; init; }

    /// <summary>
    /// Preferred segment size in bytes.
    /// Host should try to allocate this size when possible.
    /// </summary>
    public required int PreferredBytes { get; init; }

    /// <summary>
    /// Maximum segment size in bytes that the plugin is prepared to handle.
    /// </summary>
    public required int MaxBytes { get; init; }

    /// <summary>
    /// Whether the plugin supports switching writers at runtime (segment upgrade).
    /// </summary>
    public bool SupportsWriterSwitch { get; init; } = true;

    /// <summary>
    /// Suggested growth step in bytes when requesting upgrades.
    /// Host may ignore this.
    /// </summary>
    public int GrowthStepBytes { get; init; } = 0;

    public void Validate()
    {
        if (MinBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinBytes));
        if (PreferredBytes < MinBytes)
            throw new ArgumentOutOfRangeException(nameof(PreferredBytes), "PreferredBytes must be >= MinBytes");
        if (MaxBytes < PreferredBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxBytes), "MaxBytes must be >= PreferredBytes");
    }
}

/// <summary>
/// Host response for a shared memory allocation request.
/// </summary>
public sealed record SharedMemoryGrant
{
    /// <summary>
    /// Allocated segment size in bytes.
    /// </summary>
    public required int AllocatedBytes { get; init; }

    /// <summary>
    /// Whether the host will allow future upgrades for this session.
    /// </summary>
    public bool GrowthAllowed { get; init; }

    /// <summary>
    /// Optional rationale for downgrade/deny decisions.
    /// </summary>
    public string? Note { get; init; }
}
