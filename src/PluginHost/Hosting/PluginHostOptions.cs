namespace ComCross.PluginHost.Hosting;

internal sealed record PluginHostOptions(
    string PipeName,
    string PluginPath,
    string EntryPoint,
    string Role,
    string PluginId,
    string? FixedSessionId,
    string? EventPipeName,
    string? HostToken,
    string? LogDir,
    string? LogFormat,
    string? LogMinLevel);
