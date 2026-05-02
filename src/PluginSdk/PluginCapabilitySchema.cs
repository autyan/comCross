namespace ComCross.PluginSdk;

/// <summary>
/// Session host process model for a capability.
///
/// NOTE: This is a capability-level declaration (not plugin-level) because a single plugin may expose
/// both dedicated (per-session) and shared (multi-session) capabilities.
/// </summary>
public enum SessionHostModel
{
    /// <summary>
    /// Unspecified (backward compatibility).
    /// If <see cref="PluginCapabilityDescriptor.SupportsMultiSession"/> is true -> <see cref="SharedPerCapability"/>,
    /// otherwise -> <see cref="DedicatedPerSession"/>.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Default: one session host process per session.
    /// </summary>
    DedicatedPerSession = 1,

    /// <summary>
    /// Multiple sessions share one session host process per (plugin, capability).
    /// </summary>
    SharedPerCapability = 2,

    /// <summary>
    /// Multiple sessions share one session host process per logical scope key.
    /// Typically used by listener-style capabilities where child sessions must share listener state.
    /// The scope key is read from parameters via <see cref="PluginCapabilityDescriptor.SessionHostGroupKeyParameter"/>.
    /// </summary>
    SharedPerScope = 3
}

/// <summary>
/// A schema-driven capability descriptor.
/// The main process uses these descriptors to render UI and to send standardized commands.
/// </summary>
public sealed record PluginCapabilityDescriptor
{
    /// <summary>
    /// Capability id (stable within a plugin), e.g. "serial", "tcp-client", "udp".
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional icon id for UI.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// JSON Schema (draft-agnostic string) describing parameters.
    /// The host treats it as an opaque schema payload.
    /// </summary>
    public string? JsonSchema { get; init; }

    /// <summary>
    /// Optional UI schema payload (form layout hints).
    /// </summary>
    public string? UiSchema { get; init; }

    /// <summary>
    /// Optional default parameters as JSON.
    /// </summary>
    public string? DefaultParametersJson { get; init; }

    /// <summary>
    /// Shared memory request suggestion for sessions created by this capability.
    /// </summary>
    public SharedMemoryRequest? SharedMemoryRequest { get; init; }

    /// <summary>
    /// Optional declaration for capabilities where one committed parameter identifies an exclusive
    /// local resource. Hosts may use this to warn before connecting another session to the same
    /// resource, but the plugin remains the authority for final connection success/failure.
    /// </summary>
    public PluginConnectionResourceDescriptor? ConnectionResource { get; init; }

    /// <summary>
    /// Session host process model for this capability.
    /// When <see cref="SessionHostModel.Unspecified"/>, the host falls back to <see cref="SupportsMultiSession"/>.
    /// </summary>
    public SessionHostModel SessionHostModel { get; init; } = SessionHostModel.Unspecified;

    /// <summary>
    /// For <see cref="ComCross.PluginSdk.SessionHostModel.SharedPerScope"/>: the parameter name whose value is used
    /// as the session-host grouping key (e.g. "listenerSessionId").
    ///
    /// If not provided or missing at runtime, the host should fall back to current sessionId.
    /// </summary>
    public string? SessionHostGroupKeyParameter { get; init; }

    /// <summary>
    /// Whether this capability supports multiple concurrent sessions within a single plugin host process.
    /// Default is false for backward compatibility.
    /// </summary>
    public bool SupportsMultiSession { get; init; } = false;
}

public sealed record PluginConnectionResourceDescriptor(
    string ParameterKey,
    string? LabelKey = null,
    bool PromptDisconnectExisting = true);

/// <summary>
/// Plugins that expose capabilities to the host.
/// For BusAdapter plugins, capabilities typically represent connectable bus modes.
/// </summary>
public interface IPluginCapabilityProvider
{
    IReadOnlyList<PluginCapabilityDescriptor> GetCapabilities();
}
