using System.Text.Json;

namespace ComCross.PluginSdk;

public sealed record PluginConnectCommand(
    string CapabilityId,
    JsonElement Parameters,
    string SessionId,
    string? ScopeSessionId = null,
    string? ResourceKind = null,
    string? ResourceId = null);

public sealed record PluginDisconnectCommand(
    string SessionId,
    string? Reason = null);

public sealed record PluginCommandResult(
    bool Ok,
    string? Error = null,
    int BytesWritten = 0,
    string? ErrorCode = null,
    string? TargetId = null,
    bool TargetInvalidated = false);

public sealed record PluginConnectResult(
    bool Ok,
    string? Error = null,
    string? SessionId = null,
    JsonElement? CommittedParameters = null,
    string? DisplayTitle = null,
    string? DisplaySubtitle = null,
    string? ParentSessionId = null,
    string? SessionIcon = null,
    bool? CanReconnect = null,
    IReadOnlyList<string>? ManagedResourceKinds = null);
