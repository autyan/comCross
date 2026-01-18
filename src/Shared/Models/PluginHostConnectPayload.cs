using System.Text.Json;

namespace ComCross.Shared.Models;

public sealed record PluginHostConnectPayload(
    string CapabilityId,
    JsonElement Parameters,
    string SessionId);
