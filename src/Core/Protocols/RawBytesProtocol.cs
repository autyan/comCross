using System.Text;

namespace ComCross.Core.Protocols;

/// <summary>
/// 原始字节协议 - 不做任何解析，仅显示原始字节
/// 性能目标: <1μs
/// </summary>
public sealed class RawBytesProtocol : IProtocolParser
{
    public string ProtocolId => "raw-bytes";
    
    public string Name => "原始字节";
    
    public string Version => "1.0.0";
    
    public ProtocolMessage Parse(ReadOnlySpan<byte> rawData)
    {
        // 快速路径：直接转换为十六进制字符串
        // 每个字节转换为两位十六进制（如 "FF AB 12"）
        var hexString = Convert.ToHexString(rawData);
        
        // 添加空格分隔，提高可读性
        var sb = new StringBuilder(hexString.Length + hexString.Length / 2);
        for (int i = 0; i < hexString.Length; i += 2)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(hexString[i]);
            sb.Append(hexString[i + 1]);
        }
        
        return new ProtocolMessage
        {
            ProtocolId = ProtocolId,
            Content = sb.ToString(),
            RawData = rawData.ToArray(),
            IsValid = true
        };
    }
}
