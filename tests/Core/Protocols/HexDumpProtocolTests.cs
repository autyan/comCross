using System.Text;
using ComCross.Core.Protocols;
using Xunit;

namespace ComCross.Tests.Core.Protocols;

public class HexDumpProtocolTests
{
    private readonly HexDumpProtocol _protocol = new();
    
    [Fact]
    public void Parse_EmptyData_ReturnsEmptyString()
    {
        // Arrange
        var data = Array.Empty<byte>();
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.Content);
    }
    
    [Fact]
    public void Parse_SingleByte_FormatsSingleLine()
    {
        // Arrange
        var data = new byte[] { 0x48 }; // 'H'
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Contains("00000000", result.Content); // 地址偏移
        Assert.Contains("48", result.Content); // 十六进制
        Assert.Contains("|H               |", result.Content); // ASCII部分（填充15个空格）
    }
    
    [Fact]
    public void Parse_HelloWorld_FormatsCorrectly()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Hello World"); // 11字节
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        
        // 验证地址
        Assert.Contains("00000000", result.Content);
        
        // 验证十六进制部分
        Assert.Contains("48 65 6C 6C 6F 20 57 6F 72 6C 64", result.Content);
        
        // 验证ASCII部分
        Assert.Contains("|Hello World     |", result.Content);
    }
    
    [Fact]
    public void Parse_ExactlyOneLine_NoTrailingNewline()
    {
        // Arrange - 刚好16字节
        var data = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.DoesNotContain("\n", result.Content); // 单行不应有换行符
    }
    
    [Fact]
    public void Parse_MultipleLines_HasNewlines()
    {
        // Arrange - 32字节，应该产生两行
        var data = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Contains("00000000", result.Content); // 第一行地址
        Assert.Contains("00000010", result.Content); // 第二行地址（16的十六进制）
        Assert.Contains("\n", result.Content); // 应该有换行
        
        // 验证行数
        Assert.NotNull(result.Fields);
        Assert.Equal(2, result.Fields["Lines"]);
    }
    
    [Fact]
    public void Parse_NonPrintableCharacters_ShowAsDots()
    {
        // Arrange - 控制字符（0x00-0x1F）和DEL（0x7F）应该显示为'.'
        var data = new byte[] { 0x00, 0x01, 0x1F, 0x7F, 0xFF };
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        
        // ASCII部分应该全是点（非可打印字符）
        var asciiPart = result.Content.Split('|')[1];
        Assert.All(asciiPart.Take(5), c => Assert.Equal('.', c));
    }
    
    [Fact]
    public void Parse_PrintableCharacters_ShowAsIs()
    {
        // Arrange - ASCII可打印字符（32-126）
        var data = Encoding.ASCII.GetBytes("ABC123!@#");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Contains("|ABC123!@#", result.Content);
    }
    
    [Fact]
    public void Parse_LargeData_FormatsMultipleLines()
    {
        // Arrange - 100字节数据
        var data = Enumerable.Range(0, 100).Select(i => (byte)(i % 256)).ToArray();
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        
        // 应该有7行（100 / 16 = 6余4，所以是7行）
        Assert.NotNull(result.Fields);
        Assert.Equal(7, result.Fields["Lines"]);
        Assert.Equal(100, result.Fields["TotalBytes"]);
        
        // 验证所有地址偏移
        Assert.Contains("00000000", result.Content); // 第1行
        Assert.Contains("00000010", result.Content); // 第2行
        Assert.Contains("00000020", result.Content); // 第3行
        Assert.Contains("00000060", result.Content); // 第7行（96的十六进制）
    }
    
    [Fact]
    public void Parse_PartialLastLine_PaddedCorrectly()
    {
        // Arrange - 20字节（16 + 4），最后一行只有4字节
        var data = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        
        // 最后一行应该有填充空格
        var lines = result.Content.Split('\n');
        var lastLine = lines[^1];
        
        // 最后一行应该有地址0x10
        Assert.Contains("00000010", lastLine);
        
        // 应该有ASCII分隔符
        Assert.Contains("|", lastLine);
    }
    
    [Fact]
    public void ProtocolMetadata_HasCorrectValues()
    {
        // Assert
        Assert.Equal("hex-dump", _protocol.ProtocolId);
        Assert.Equal("十六进制转储", _protocol.Name);
        Assert.Equal("1.0.0", _protocol.Version);
    }
    
    [Fact]
    public void Parse_RawDataField_ContainsOriginalBytes()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03 };
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.NotNull(result.RawData);
        Assert.Equal(data, result.RawData);
    }
}
