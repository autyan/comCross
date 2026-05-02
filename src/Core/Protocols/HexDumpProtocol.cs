using System.Text;

namespace ComCross.Core.Protocols;

/// <summary>
/// 十六进制转储协议 - 标准hex dump格式
/// 格式: 00000000  48 65 6C 6C 6F 20 57 6F 72 6C 64  |Hello World|
/// 性能目标: <5μs（因为有格式化开销）
/// </summary>
public sealed class HexDumpProtocol : IProtocolParser
{
    private const int BytesPerLine = 16;
    
    public string ProtocolId => "hex-dump";
    
    public string Name => "十六进制转储";
    
    public string Version => "1.0.0";
    
    public ProtocolMessage Parse(ReadOnlySpan<byte> rawData)
    {
        if (rawData.Length == 0)
        {
            return new ProtocolMessage
            {
                ProtocolId = ProtocolId,
                Content = string.Empty,
                RawData = Array.Empty<byte>(),
                IsValid = true
            };
        }
        
        // 计算需要的行数
        int lineCount = (rawData.Length + BytesPerLine - 1) / BytesPerLine;
        
        // 预分配StringBuilder容量
        // 每行约80字符：8(地址) + 2(空格) + 48(hex) + 2(空格) + 1(|) + 16(ascii) + 1(|) + 1(\n)
        var sb = new StringBuilder(lineCount * 80);
        
        for (int offset = 0; offset < rawData.Length; offset += BytesPerLine)
        {
            // 地址偏移（8位十六进制）
            sb.AppendFormat("{0:X8}  ", offset);
            
            // 十六进制部分
            int lineLength = Math.Min(BytesPerLine, rawData.Length - offset);
            for (int i = 0; i < BytesPerLine; i++)
            {
                if (i < lineLength)
                {
                    sb.AppendFormat("{0:X2} ", rawData[offset + i]);
                }
                else
                {
                    sb.Append("   "); // 填充空白
                }
            }
            
            // ASCII表示部分
            sb.Append(" |");
            for (int i = 0; i < lineLength; i++)
            {
                byte b = rawData[offset + i];
                // 可打印ASCII字符（32-126），其他用'.'代替
                char c = (b >= 32 && b <= 126) ? (char)b : '.';
                sb.Append(c);
            }
            
            // 填充剩余空白
            for (int i = lineLength; i < BytesPerLine; i++)
            {
                sb.Append(' ');
            }
            
            sb.Append('|');
            
            // 非最后一行时添加换行
            if (offset + BytesPerLine < rawData.Length)
            {
                sb.AppendLine();
            }
        }
        
        return new ProtocolMessage
        {
            ProtocolId = ProtocolId,
            Content = sb.ToString(),
            RawData = rawData.ToArray(),
            IsValid = true,
            Fields = new Dictionary<string, object>
            {
                ["TotalBytes"] = rawData.Length,
                ["Lines"] = lineCount
            }
        };
    }
}
