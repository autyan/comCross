using System.Text.Json;

namespace ComCross.Shared.Models;

/// <summary>
/// White-listed action request emitted by ExtensionHost and routed into Core.
/// </summary>
public sealed record PluginHostExtensionActionRequestEvent(
    string PluginId,
    string Action,
    string? SessionId = null,
    JsonElement? Payload = null);
