using System.Text;
using ComCross.Core.Protocols;
using Xunit;

namespace ComCross.Tests.Core.Protocols;

public class RawBytesProtocolTests
{
    private readonly RawBytesProtocol _protocol = new();
    
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
        Assert.Equal("raw-bytes", result.ProtocolId);
    }
    
    [Fact]
    public void Parse_SingleByte_ReturnsHexString()
    {
        // Arrange
        var data = new byte[] { 0xFF };
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("FF", result.Content);
    }
    
    [Fact]
    public void Parse_MultipleBytes_ReturnsSpaceSeparatedHex()
    {
        // Arrange
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("48 65 6C 6C 6F", result.Content);
        Assert.Equal(data, result.RawData);
    }
    
    [Fact]
    public void Parse_BinaryData_HandlesAllByteValues()
    {
        // Arrange - 测试0x00到0xFF所有字节值
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        
        // Act
        var result = _protocol.Parse(data);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.Content);
        Assert.Equal(data.Length, result.RawData!.Length);
        
        // 验证格式：每两个字符后有一个空格（除了最后）
        var expectedLength = 256 * 2 + 255; // 256字节 * 2位 + 255个空格
        Assert.Equal(expectedLength, result.Content.Length);
    }
    
    [Fact]
    public void ProtocolMetadata_HasCorrectValues()
    {
        // Assert
        Assert.Equal("raw-bytes", _protocol.ProtocolId);
        Assert.Equal("原始字节", _protocol.Name);
        Assert.Equal("1.0.0", _protocol.Version);
    }
}
