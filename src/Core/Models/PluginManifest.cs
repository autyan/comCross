namespace ComCross.Core.Services;

public sealed class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "0.0.0";
    public string TargetCoreVersion { get; set; } = "0.2";
    public string EntryPoint { get; set; } = string.Empty;
    public string ToolGroup { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

public sealed class PluginInfo
{
    public required string AssemblyPath { get; init; }
    public required PluginManifest Manifest { get; init; }
}
