using System.Collections.Concurrent;

namespace ComCross.Core.Protocols;

/// <summary>
/// 协议注册表 - 管理所有协议解析器
/// 支持内置协议和动态加载的脚本协议
/// </summary>
public sealed class ProtocolRegistry
{
    private readonly ConcurrentDictionary<string, IProtocolParser> _parsers = new();
    
    public ProtocolRegistry()
    {
        // 注册内置协议
        RegisterBuiltInProtocols();
    }
    
    /// <summary>
    /// 注册内置协议
    /// </summary>
    private void RegisterBuiltInProtocols()
    {
        Register(new RawBytesProtocol());
        Register(new AsciiTextProtocol());
        Register(new HexDumpProtocol());
    }
    
    /// <summary>
    /// 注册协议解析器
    /// </summary>
    public void Register(IProtocolParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        
        if (!_parsers.TryAdd(parser.ProtocolId, parser))
        {
            throw new InvalidOperationException($"协议 '{parser.ProtocolId}' 已注册");
        }
    }
    
    /// <summary>
    /// 取消注册协议
    /// </summary>
    public bool Unregister(string protocolId)
    {
        return _parsers.TryRemove(protocolId, out _);
    }
    
    /// <summary>
    /// 获取协议解析器
    /// </summary>
    public IProtocolParser? GetParser(string protocolId)
    {
        _parsers.TryGetValue(protocolId, out var parser);
        return parser;
    }
    
    /// <summary>
    /// 获取所有已注册的协议
    /// </summary>
    public IReadOnlyCollection<IProtocolParser> GetAllParsers()
    {
        return _parsers.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// 检查协议是否已注册
    /// </summary>
    public bool Contains(string protocolId)
    {
        return _parsers.ContainsKey(protocolId);
    }
}
