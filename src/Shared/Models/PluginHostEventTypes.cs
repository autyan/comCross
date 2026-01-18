namespace ComCross.Shared.Models;

public static class PluginHostEventTypes
{
    public const string UiStateInvalidated = "ui-state-invalidated";

    // Phase 0+ lifecycle
    public const string HostRegistered = "host-registered";

    // ADR-010 MVP: per-session binding handshake
    public const string SessionRegistered = "session-registered";
}
