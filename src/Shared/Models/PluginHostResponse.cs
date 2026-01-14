namespace ComCross.Shared.Models;

public sealed record PluginHostResponse(
    string Id,
    bool Ok,
    string? Error = null,
    bool Restarted = false);
