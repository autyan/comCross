namespace ComCross.Shared.Models;

/// <summary>
/// Apply the shared-memory segment descriptor for a specific session.
///
/// ADR-010 contract: the main process owns segment allocation and sends a cross-process descriptor.
/// The PluginHost is responsible for reopening the mapping and switching the plugin writer.
/// </summary>
public sealed record PluginHostApplySharedMemorySegmentPayload(
    string SessionId,
    SharedMemorySegmentDescriptor Descriptor);
