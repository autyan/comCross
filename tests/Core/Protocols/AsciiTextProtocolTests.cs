using System.Text;
using ComCross.Core.Protocols;
using Xunit;

namespace ComCross.Tests.Core.Protocols;

public class AsciiTextProtocolTests
{
    private readonly AsciiTextProtocol _protocol = new();
    
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
    public void Parse_SimpleText_ReturnsDecodedString()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Hello World");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Hello World", result.Content);
        Assert.NotNull(result.Fields);
        Assert.Equal(1, result.Fields["LineCount"]); // 单行
    }
    
    [Fact]
    public void Parse_TextWithCRLF_NormalizesToLF()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Line1\r\nLine2\r\nLine3");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Line1\nLine2\nLine3", result.Content);
        Assert.NotNull(result.Fields);
        Assert.Equal(3, result.Fields["LineCount"]); // 三行
    }
    
    [Fact]
    public void Parse_TextWithCR_NormalizesToLF()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Line1\rLine2\rLine3");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Line1\nLine2\nLine3", result.Content);
    }
    
    [Fact]
    public void Parse_TextWithLF_PreservesLF()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Line1\nLine2\nLine3");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Line1\nLine2\nLine3", result.Content);
    }
    
    [Fact]
    public void Parse_TextEndingWithNewline_CorrectLineCount()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Line1\nLine2\n");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.NotNull(result.Fields);
        Assert.Equal(2, result.Fields["LineCount"]); // 两行（结尾换行不算新行）
    }
    
    [Fact]
    public void Parse_ControlCharacters_Preserved()
    {
        // Arrange - 包含制表符、换行等控制字符
        var data = Encoding.ASCII.GetBytes("Tab:\tNewline:\nEnd");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Contains("\t", result.Content);
        Assert.Contains("\n", result.Content);
    }
    
    [Fact]
    public void Parse_NonAsciiBytes_ReplacedWithQuestionMark()
    {
        // Arrange - ASCII解码器会将非ASCII字节（>127）替换为'?'
        var data = new byte[] { 0x48, 0x65, 0xFF, 0x6C, 0x6F }; // He�lo
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("He?lo", result.Content);
    }
    
    [Fact]
    public void ProtocolMetadata_HasCorrectValues()
    {
        // Assert
        Assert.Equal("ascii-text", _protocol.ProtocolId);
        Assert.Equal("ASCII文本", _protocol.Name);
        Assert.Equal("1.0.0", _protocol.Version);
    }
    
    [Fact]
    public void Parse_Fields_ContainsLength()
    {
        // Arrange
        var data = Encoding.ASCII.GetBytes("Hello");
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.NotNull(result.Fields);
        Assert.Equal(5, result.Fields["Length"]);
    }
}
