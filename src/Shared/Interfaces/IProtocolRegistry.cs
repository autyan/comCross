using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Protocol registry for managing protocol parsers.
/// Provides registration, discovery, and lifecycle management for protocols.
/// </summary>
public interface IProtocolRegistry
{
    /// <summary>
    /// Register a protocol parser.
    /// </summary>
    /// <param name="parser">Parser to register</param>
    /// <exception cref="ArgumentNullException">Parser is null</exception>
    /// <exception cref="InvalidOperationException">Protocol ID already registered</exception>
    void Register(IMessageParser parser);
    
    /// <summary>
    /// Register multiple protocol parsers.
    /// </summary>
    /// <param name="parsers">Parsers to register</param>
    void RegisterRange(IEnumerable<IMessageParser> parsers);
    
    /// <summary>
    /// Unregister a protocol parser.
    /// </summary>
    /// <param name="protocolId">Protocol ID to unregister</param>
    /// <returns>True if protocol was unregistered, false if not found</returns>
    /// <exception cref="InvalidOperationException">Attempting to unregister built-in protocol</exception>
    bool Unregister(string protocolId);
    
    /// <summary>
    /// Get a protocol parser by ID.
    /// </summary>
    /// <param name="protocolId">Protocol ID to retrieve</param>
    /// <returns>Parser instance</returns>
    /// <exception cref="KeyNotFoundException">Protocol not found</exception>
    IMessageParser GetParser(string protocolId);
    
    /// <summary>
    /// Try to get a protocol parser by ID.
    /// </summary>
    /// <param name="protocolId">Protocol ID to retrieve</param>
    /// <param name="parser">Parser instance if found</param>
    /// <returns>True if parser was found</returns>
    bool TryGetParser(string protocolId, out IMessageParser? parser);
    
    /// <summary>
    /// Get metadata for all registered protocols.
    /// </summary>
    IReadOnlyList<ProtocolMetadata> GetAll();
    
    /// <summary>
    /// Get metadata for built-in protocols only.
    /// </summary>
    IReadOnlyList<ProtocolMetadata> GetBuiltIn();
    
    /// <summary>
    /// Get metadata for custom (non-built-in) protocols only.
    /// </summary>
    IReadOnlyList<ProtocolMetadata> GetCustom();
    
    /// <summary>
    /// Check if a protocol is registered.
    /// </summary>
    bool IsRegistered(string protocolId);
    
    /// <summary>
    /// Get the number of registered protocols.
    /// </summary>
    int Count { get; }
    
    /// <summary>
    /// Event raised when a protocol is registered.
    /// </summary>
    event EventHandler<ProtocolRegisteredEventArgs>? ProtocolRegistered;
    
    /// <summary>
    /// Event raised when a protocol is unregistered.
    /// </summary>
    event EventHandler<ProtocolUnregisteredEventArgs>? ProtocolUnregistered;
}

/// <summary>
/// Event arguments for protocol registration.
/// </summary>
public sealed class ProtocolRegisteredEventArgs : EventArgs
{
    public string ProtocolId { get; init; } = string.Empty;
    public string ProtocolName { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
}

/// <summary>
/// Event arguments for protocol unregistration.
/// </summary>
public sealed class ProtocolUnregisteredEventArgs : EventArgs
{
    public string ProtocolId { get; init; } = string.Empty;
    public string ProtocolName { get; init; } = string.Empty;
}
