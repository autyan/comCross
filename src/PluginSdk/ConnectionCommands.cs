using System.Text.Json;

namespace ComCross.PluginSdk;

public sealed record PluginConnectCommand(
    string CapabilityId,
    JsonElement Parameters,
    string SessionId);

public sealed record PluginDisconnectCommand(
    string SessionId,
    string? Reason = null);

public sealed record PluginCommandResult(
    bool Ok,
    string? Error = null);

public sealed record PluginConnectResult(
    bool Ok,
    string? Error = null,
    string? SessionId = null);
