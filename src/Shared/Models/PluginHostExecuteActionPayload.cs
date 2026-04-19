using System.Text.Json;

namespace ComCross.Shared.Models;

public sealed record PluginHostExecuteActionPayload(
    string ActionName,
    JsonElement? Parameters = null);
