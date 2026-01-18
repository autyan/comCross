using ComCross.PluginSdk;

namespace ComCross.Shared.Models;

public sealed record PluginHostSetBackpressurePayload(
    string SessionId,
    BackpressureLevel Level);
