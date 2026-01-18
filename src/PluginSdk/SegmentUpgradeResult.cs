namespace ComCross.PluginSdk;

/// <summary>
/// Result for a shared-memory segment upgrade request.
/// </summary>
public sealed record SegmentUpgradeResult(
    bool Ok,
    string? Error = null,
    int? GrantedBytes = null);
