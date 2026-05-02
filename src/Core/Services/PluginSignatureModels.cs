using System.Text.Json.Serialization;

namespace ComCross.Core.Services;

public sealed class PluginPackageSignature
{
    public int SchemaVersion { get; set; }

    public string KeyId { get; set; } = string.Empty;

    public string PluginId { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Algorithm { get; set; } = string.Empty;

    public DateTimeOffset SignedAt { get; set; }

    public List<PluginPackageSignatureFile> Files { get; set; } = new();

    public string Signature { get; set; } = string.Empty;
}

public sealed class PluginPackageSignatureFile
{
    public string Path { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public sealed record PluginPackageSignaturePayload(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("signedAt")] DateTimeOffset SignedAt,
    [property: JsonPropertyName("files")] IReadOnlyList<PluginPackageSignatureFilePayload> Files);

public sealed record PluginPackageSignatureFilePayload(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256);
