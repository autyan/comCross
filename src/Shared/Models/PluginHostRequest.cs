namespace ComCross.Shared.Models;

using System.Text.Json;

public sealed record PluginHostRequest(
    string Id,
    string Type,
    PluginNotification? Notification = null,
    JsonElement? Payload = null);
