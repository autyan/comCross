using System.Text.Json;

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

    /// <summary>
    /// Optional plugin-provided UI i18n bundles.
    /// Format: cultureCode -> (key -> value)
    ///
    /// Keys MUST be prefixed with "{pluginId}." to avoid collisions.
    /// The host will not overwrite existing keys and will emit notifications for duplicates.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? I18n { get; set; }

    /// <summary>
    /// Optional plugin settings page contributions.
    /// These pages are rendered by the host Settings UI.
    /// </summary>
    public List<PluginSettingsPageManifest>? SettingsPages { get; set; }
}

public sealed class PluginSettingsPageManifest
{
    public string Id { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    // Declarative UI schema (UiSchemaVersion1) and optional JSON schema.
    public JsonElement? UiSchema { get; set; }
    public JsonElement? JsonSchema { get; set; }
}

public sealed class PluginInfo
{
    public required string AssemblyPath { get; init; }
    public required PluginManifest Manifest { get; init; }
}
