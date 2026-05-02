namespace ComCross.Shared.Models;

/// <summary>
/// Registration handshake event emitted by a host process (PluginHost/UIHost/SessionHost)
/// to prove identity and allow the main process to bind runtime state.
/// </summary>
public sealed record PluginHostRegisteredEvent(
    string Token,
    int ProcessId);
