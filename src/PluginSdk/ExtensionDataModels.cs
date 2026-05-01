using System.Text.Json;

namespace ComCross.PluginSdk;

public sealed record ExtensionFrame(
    long FrameId,
    string SessionId,
    DateTime TimestampUtc,
    string Direction,
    byte[] RawData,
    string Format,
    string Source);

public sealed record ExtensionSessionSnapshot(
    string Id,
    string Name,
    string AdapterId,
    string? PluginId,
    string? CapabilityId,
    string Status,
    string? ParentSessionId,
    string? DisplayTitle,
    string? DisplaySubtitle,
    string? DisplayIcon,
    IReadOnlyList<string> ManagedResourceKinds,
    string? ParametersJson,
    long RxBytes,
    long TxBytes);

public sealed record ExtensionWorkloadSnapshot(
    string Id,
    string Name,
    bool IsDefault,
    string? Description,
    IReadOnlyList<string> SessionIds);

public sealed record ExtensionContextSnapshot(
    IReadOnlyList<ExtensionSessionSnapshot> Sessions,
    IReadOnlyList<ExtensionWorkloadSnapshot> Workloads,
    string? ActiveWorkloadId,
    string Language,
    JsonElement Settings);
