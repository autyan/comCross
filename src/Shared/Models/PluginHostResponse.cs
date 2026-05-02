namespace ComCross.Shared.Models;

using System.Text.Json;

public sealed record PluginHostResponse(
    string Id,
    bool Ok,
    string? Error = null,
    bool Restarted = false,
    JsonElement? Payload = null);
