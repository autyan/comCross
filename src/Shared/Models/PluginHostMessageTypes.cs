namespace ComCross.Shared.Models;

public static class PluginHostMessageTypes
{
    public const string Ping = "ping";
    public const string Notify = "notify";

    // Phase 0+ standardized commands
    public const string GetCapabilities = "get-capabilities";
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string GetUiState = "get-ui-state";
    public const string ApplySharedMemorySegment = "apply-shared-memory-segment";
    public const string SetBackpressure = "set-backpressure";

    // Data plane (Session Host only)
    public const string SendData = "send-data";

    // Optional UX signals
    public const string LanguageChanged = "language-changed";

    public const string Shutdown = "shutdown";
}
