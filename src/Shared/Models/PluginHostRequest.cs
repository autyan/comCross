namespace ComCross.Shared.Models;

public sealed record PluginHostRequest(
    string Id,
    string Type,
    PluginNotification? Notification = null);
