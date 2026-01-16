using System.Collections.Concurrent;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Default implementation of protocol registry.
/// Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public sealed class ProtocolRegistry : IProtocolRegistry
{
    private readonly ConcurrentDictionary<string, IMessageParser> _parsers = new();
    private readonly ILogger<ProtocolRegistry> _logger;
    
    public ProtocolRegistry(ILogger<ProtocolRegistry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <inheritdoc/>
    public void Register(IMessageParser parser)
    {
        if (parser == null)
        {
            throw new ArgumentNullException(nameof(parser));
        }
        
        if (string.IsNullOrWhiteSpace(parser.Id))
        {
            throw new ArgumentException("Protocol ID cannot be empty", nameof(parser));
        }
        
        if (_parsers.ContainsKey(parser.Id))
        {
            throw new InvalidOperationException($"Protocol '{parser.Id}' is already registered");
        }
        
        if (!_parsers.TryAdd(parser.Id, parser))
        {
            throw new InvalidOperationException($"Failed to register protocol '{parser.Id}'");
        }
        
        _logger.LogInformation("Registered protocol: {ProtocolName} ({ProtocolId}), built-in: {IsBuiltIn}", 
            parser.Name, parser.Id, parser.IsBuiltIn);
        
        ProtocolRegistered?.Invoke(this, new ProtocolRegisteredEventArgs
        {
            ProtocolId = parser.Id,
            ProtocolName = parser.Name,
            IsBuiltIn = parser.IsBuiltIn
        });
    }
    
    /// <inheritdoc/>
    public void RegisterRange(IEnumerable<IMessageParser> parsers)
    {
        if (parsers == null)
        {
            throw new ArgumentNullException(nameof(parsers));
        }
        
        foreach (var parser in parsers)
        {
            Register(parser);
        }
    }
    
    /// <inheritdoc/>
    public bool Unregister(string protocolId)
    {
        if (string.IsNullOrWhiteSpace(protocolId))
        {
            throw new ArgumentException("Protocol ID cannot be empty", nameof(protocolId));
        }
        
        if (!_parsers.TryGetValue(protocolId, out var parser))
        {
            _logger.LogWarning("Attempted to unregister non-existent protocol: {ProtocolId}", protocolId);
            return false;
        }
        
        if (parser.IsBuiltIn)
        {
            throw new InvalidOperationException($"Cannot unregister built-in protocol '{protocolId}'");
        }
        
        if (!_parsers.TryRemove(protocolId, out _))
        {
            _logger.LogError("Failed to unregister protocol: {ProtocolId}", protocolId);
            return false;
        }
        
        _logger.LogInformation("Unregistered protocol: {ProtocolName} ({ProtocolId})", 
            parser.Name, protocolId);
        
        ProtocolUnregistered?.Invoke(this, new ProtocolUnregisteredEventArgs
        {
            ProtocolId = protocolId,
            ProtocolName = parser.Name
        });
        
        return true;
    }
    
    /// <inheritdoc/>
    public IMessageParser GetParser(string protocolId)
    {
        if (string.IsNullOrWhiteSpace(protocolId))
        {
            throw new ArgumentException("Protocol ID cannot be empty", nameof(protocolId));
        }
        
        if (!_parsers.TryGetValue(protocolId, out var parser))
        {
            throw new KeyNotFoundException($"Protocol '{protocolId}' not found");
        }
        
        return parser;
    }
    
    /// <inheritdoc/>
    public bool TryGetParser(string protocolId, out IMessageParser? parser)
    {
        if (string.IsNullOrWhiteSpace(protocolId))
        {
            parser = null;
            return false;
        }
        
        return _parsers.TryGetValue(protocolId, out parser);
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<ProtocolMetadata> GetAll()
    {
        return _parsers.Values
            .Select(ProtocolMetadata.FromParser)
            .OrderBy(m => m.IsBuiltIn ? 0 : 1) // Built-in first
            .ThenBy(m => m.Category)
            .ThenBy(m => m.Name)
            .ToList();
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<ProtocolMetadata> GetBuiltIn()
    {
        return _parsers.Values
            .Where(p => p.IsBuiltIn)
            .Select(ProtocolMetadata.FromParser)
            .OrderBy(m => m.Category)
            .ThenBy(m => m.Name)
            .ToList();
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<ProtocolMetadata> GetCustom()
    {
        return _parsers.Values
            .Where(p => !p.IsBuiltIn)
            .Select(ProtocolMetadata.FromParser)
            .OrderBy(m => m.Category)
            .ThenBy(m => m.Name)
            .ToList();
    }
    
    /// <inheritdoc/>
    public bool IsRegistered(string protocolId)
    {
        return !string.IsNullOrWhiteSpace(protocolId) && _parsers.ContainsKey(protocolId);
    }
    
    /// <inheritdoc/>
    public int Count => _parsers.Count;
    
    /// <inheritdoc/>
    public event EventHandler<ProtocolRegisteredEventArgs>? ProtocolRegistered;
    
    /// <inheritdoc/>
    public event EventHandler<ProtocolUnregisteredEventArgs>? ProtocolUnregistered;
}
