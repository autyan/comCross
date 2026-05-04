namespace ComCross.Shared.Models;

public enum MessageFrameDataSource
{
    LiveSpool,
    Archive
}

public enum MessageFrameQueryStatus
{
    Ok,
    NoFrames,
    NoMoreBefore,
    NoMoreAfter,
    DataEvicted,
    SourceUnavailable,
    ArchiveDisabled,
    ArchiveError,
    InvalidQuery
}

public enum MessageFrameQueryKind
{
    Latest,
    After,
    Before
}

public sealed record MessageFrameQuery(
    string SessionId,
    MessageFrameDataSource Source,
    MessageFrameQueryKind Kind,
    long FrameId,
    int Limit);

public sealed record MessageFrameQueryResult(
    MessageFrameQueryStatus Status,
    IReadOnlyList<MessageFrameRecord> Frames,
    long? FirstAvailableFrameId = null,
    long? LastAvailableFrameId = null,
    string? ErrorCode = null);

public enum SessionArchiveState
{
    Disabled,
    Enabled,
    Error
}

public enum MessageDisplayDensity
{
    Plain,
    Slim,
    Detailed
}

public enum PayloadRenderMode
{
    String,
    Hex
}

public enum SessionLogExportFormat
{
    Plain,
    Slim,
    DetailedJsonLines
}

public enum StorageTier
{
    Conservative,
    Limited,
    Normal,
    HighCapacity
}

public enum StorageCalibrationPhase
{
    Conservative,
    Calibrating,
    Completed,
    Failed
}

public sealed record StorageCalibrationSnapshot(
    StorageCalibrationPhase Phase,
    StorageTier Tier,
    string? FingerprintHash,
    DateTime? LastCalibratedAtUtc,
    string StorageRoot,
    string? Reason = null);

public enum StorageHealth
{
    Healthy,
    Busy,
    Degraded,
    LosingData,
    ArchiveError,
    Unavailable
}

public sealed record StorageHealthSnapshot(
    StorageHealth Health,
    StorageTier Tier,
    DateTime UpdatedAtUtc,
    string? Reason = null);

public sealed record StoragePolicy(
    StorageTier Tier,
    int SegmentSizeMb,
    bool PreallocateSegments,
    int MessagePumpBatchSize);
