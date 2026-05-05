using System.Text.Json;

namespace ComCross.PluginSdk;

public interface IPluginSessionStateInitializer
{
    Task<PluginSessionStateInitializationResult> InitializeSessionStateAsync(
        PluginSessionStateInitializationContext context,
        CancellationToken cancellationToken);
}

public sealed record PluginSessionStateInitializationContext(
    string PluginId,
    string CapabilityId,
    string SessionId,
    string? PluginVersion,
    string? PreviousPluginVersion,
    string? ParametersJson,
    PluginSessionStorageSnapshot Storage);

public sealed record PluginSessionStorageSnapshot(
    int SchemaVersion,
    IReadOnlyDictionary<string, JsonElement> Values);

public sealed record PluginSessionStateInitializationResult(
    bool Ok,
    string? Error = null,
    PluginSessionStoragePatch? StoragePatch = null,
    PluginSessionMetadataPatch? SessionPatch = null,
    bool InvalidateUiState = false);

public sealed record PluginSessionStoragePatch(
    int? SchemaVersion = null,
    IReadOnlyDictionary<string, JsonElement>? Upserts = null,
    IReadOnlyList<string>? Deletes = null);

public sealed record PluginSessionMetadataPatch(
    string? ParametersJson = null,
    string? DisplayTitle = null,
    string? DisplaySubtitle = null,
    string? DisplayIcon = null,
    bool? CanReconnect = null,
    bool? CanTransmit = null,
    string? ParentSessionId = null,
    IReadOnlyList<string>? ManagedResourceKinds = null);
