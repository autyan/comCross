namespace ComCross.PluginSdk;

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
    /// Whether this capability supports multiple concurrent sessions within a single plugin host process.
    /// Default is false for backward compatibility.
    /// </summary>
    public bool SupportsMultiSession { get; init; } = false;
}

/// <summary>
/// Plugins that expose capabilities to the host.
/// For BusAdapter plugins, capabilities typically represent connectable bus modes.
/// </summary>
public interface IPluginCapabilityProvider
{
    IReadOnlyList<PluginCapabilityDescriptor> GetCapabilities();
}
