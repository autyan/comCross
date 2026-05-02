namespace ComCross.Shared.Models;

public sealed record ExtensionHostPluginLoadInfo(
    string PluginId,
    string PluginPath,
    string EntryPoint);

public sealed record ExtensionHostPluginSyncResult(
    string PluginId,
    bool Ok,
    string? Error = null);
