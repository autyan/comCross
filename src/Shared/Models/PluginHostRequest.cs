namespace ComCross.Shared.Models;

using System.Text.Json;

public sealed record PluginHostRequest(
    string Id,
    string Type,
    string? SessionId = null,
    PluginNotification? Notification = null,
    JsonElement? Payload = null);
