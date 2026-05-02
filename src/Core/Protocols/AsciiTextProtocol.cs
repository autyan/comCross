using System.Text;

namespace ComCross.Core.Protocols;

/// <summary>
/// ASCII文本协议 - 解析ASCII文本，\r\n作为消息分隔符
/// 性能目标: <1μs
/// </summary>
public sealed class AsciiTextProtocol : IProtocolParser
{
    public string ProtocolId => "ascii-text";
    
    public string Name => "ASCII文本";
    
    public string Version => "1.0.0";
    
    public ProtocolMessage Parse(ReadOnlySpan<byte> rawData)
    {
        try
        {
            // 快速路径：直接解码为ASCII字符串
            // 使用Encoding.ASCII以获得最佳性能
            var text = Encoding.ASCII.GetString(rawData);
            
            // 标准化行尾：\r\n -> \n, \r -> \n
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            
            return new ProtocolMessage
            {
                ProtocolId = ProtocolId,
                Content = text,
                RawData = rawData.ToArray(),
                IsValid = true,
                Fields = new Dictionary<string, object>
                {
                    ["LineCount"] = text.Count(c => c == '\n') + (text.Length > 0 && text[^1] != '\n' ? 1 : 0),
                    ["Length"] = text.Length
                }
            };
        }
        catch (Exception ex)
        {
            return new ProtocolMessage
            {
                ProtocolId = ProtocolId,
                Content = string.Empty,
                RawData = rawData.ToArray(),
                IsValid = false,
                ErrorMessage = $"ASCII解码失败: {ex.Message}"
            };
        }
    }
}
