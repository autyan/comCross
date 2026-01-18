using System.Text.Json;

namespace ComCross.Shared.Models;

/// <summary>
/// One-way event message emitted by PluginHost to the main process via the event-stream pipe.
/// </summary>
public sealed record PluginHostEvent(
    string Type,
    JsonElement? Payload = null);
