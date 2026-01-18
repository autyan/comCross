namespace ComCross.Shared.Models;

/// <summary>
/// Legacy payload model for the deprecated "request-segment-upgrade" IPC command.
///
/// ADR-010 breaking change: replaced by <see cref="PluginHostApplySharedMemorySegmentPayload" />
/// and the "apply-shared-memory-segment" command.
/// </summary>
[Obsolete("Replaced by PluginHostApplySharedMemorySegmentPayload (apply-shared-memory-segment).", error: true)]
public sealed record PluginHostRequestSegmentUpgradePayload(
    string SessionId,
    int RequestedBytes,
    SharedMemorySegmentDescriptor Descriptor);
