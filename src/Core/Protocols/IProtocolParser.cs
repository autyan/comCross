namespace ComCross.Core.Protocols;

/// <summary>
/// 协议解析器接口
/// 内置协议通过C#实现此接口
/// 未来支持DSL/JS/C#脚本动态加载
/// </summary>
public interface IProtocolParser
{
    /// <summary>
    /// 协议唯一标识
    /// </summary>
    string ProtocolId { get; }
    
    /// <summary>
    /// 协议名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 协议版本
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// 解析物理帧为协议消息
    /// </summary>
    /// <param name="rawData">原始字节数据</param>
    /// <returns>解析后的消息内容</returns>
    ProtocolMessage Parse(ReadOnlySpan<byte> rawData);
}

/// <summary>
/// 协议解析结果
/// </summary>
public class ProtocolMessage
{
    /// <summary>
    /// 协议ID
    /// </summary>
    public required string ProtocolId { get; init; }
    
    /// <summary>
    /// 显示内容（主要显示文本）
    /// </summary>
    public required string Content { get; init; }
    
    /// <summary>
    /// 原始数据
    /// </summary>
    public byte[]? RawData { get; init; }
    
    /// <summary>
    /// 结构化字段（可选，用于高级显示）
    /// </summary>
    public Dictionary<string, object>? Fields { get; init; }
    
    /// <summary>
    /// 是否解析成功
    /// </summary>
    public bool IsValid { get; init; } = true;
    
    /// <summary>
    /// 错误信息（解析失败时）
    /// </summary>
    public string? ErrorMessage { get; init; }
}
