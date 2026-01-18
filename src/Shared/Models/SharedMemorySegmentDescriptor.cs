namespace ComCross.Shared.Models;

/// <summary>
/// Cross-process descriptor for a session's shared-memory segment.
///
/// The receiver can reopen the underlying mapping and create a view accessor
/// for the specified segment range.
/// </summary>
public sealed record SharedMemorySegmentDescriptor(
    string MapName,
    long MapCapacityBytes,
    string? UnixFilePath,
    long SegmentOffset,
    int SegmentSize);
