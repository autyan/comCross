using ComCross.Core.Protocols;
using Xunit;

namespace ComCross.Tests.Core.Protocols;

public class ProtocolRegistryTests
{
    [Fact]
    public void Constructor_RegistersBuiltInProtocols()
    {
        // Act
        var registry = new ProtocolRegistry();
        
        // Assert - 应该有三个内置协议
        var allParsers = registry.GetAllParsers();
        Assert.Equal(3, allParsers.Count);
        
        Assert.True(registry.Contains("raw-bytes"));
        Assert.True(registry.Contains("ascii-text"));
        Assert.True(registry.Contains("hex-dump"));
    }
    
    [Fact]
    public void GetParser_ExistingProtocol_ReturnsParser()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        
        // Act
        var parser = registry.GetParser("raw-bytes");
        
        // Assert
        Assert.NotNull(parser);
        Assert.Equal("raw-bytes", parser.ProtocolId);
        Assert.IsType<RawBytesProtocol>(parser);
    }
    
    [Fact]
    public void GetParser_NonExistingProtocol_ReturnsNull()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        
        // Act
        var parser = registry.GetParser("non-existing");
        
        // Assert
        Assert.Null(parser);
    }
    
    [Fact]
    public void Register_NewProtocol_Success()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var customParser = new TestProtocol("custom-protocol");
        
        // Act
        registry.Register(customParser);
        
        // Assert
        Assert.True(registry.Contains("custom-protocol"));
        var retrieved = registry.GetParser("custom-protocol");
        Assert.Same(customParser, retrieved);
    }
    
    [Fact]
    public void Register_DuplicateProtocolId_ThrowsException()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var parser1 = new TestProtocol("duplicate-id");
        var parser2 = new TestProtocol("duplicate-id");
        
        registry.Register(parser1);
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(parser2));
        Assert.Contains("duplicate-id", ex.Message);
        Assert.Contains("已注册", ex.Message);
    }
    
    [Fact]
    public void Register_NullParser_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }
    
    [Fact]
    public void Unregister_ExistingProtocol_ReturnsTrue()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var parser = new TestProtocol("temp-protocol");
        registry.Register(parser);
        
        // Act
        var result = registry.Unregister("temp-protocol");
        
        // Assert
        Assert.True(result);
        Assert.False(registry.Contains("temp-protocol"));
    }
    
    [Fact]
    public void Unregister_NonExistingProtocol_ReturnsFalse()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        
        // Act
        var result = registry.Unregister("non-existing");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void GetAllParsers_ReturnsAllRegisteredParsers()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var custom1 = new TestProtocol("custom-1");
        var custom2 = new TestProtocol("custom-2");
        
        registry.Register(custom1);
        registry.Register(custom2);
        
        // Act
        var allParsers = registry.GetAllParsers();
        
        // Assert
        Assert.Equal(5, allParsers.Count); // 3个内置 + 2个自定义
        Assert.Contains(allParsers, p => p.ProtocolId == "custom-1");
        Assert.Contains(allParsers, p => p.ProtocolId == "custom-2");
    }
    
    [Fact]
    public void Contains_ExistingProtocol_ReturnsTrue()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        
        // Act & Assert
        Assert.True(registry.Contains("raw-bytes"));
    }
    
    [Fact]
    public void Contains_NonExistingProtocol_ReturnsFalse()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        
        // Act & Assert
        Assert.False(registry.Contains("non-existing"));
    }
    
    // 测试用自定义协议
    private class TestProtocol : IProtocolParser
    {
        public TestProtocol(string protocolId)
        {
            ProtocolId = protocolId;
        }
        
        public string ProtocolId { get; }
        public string Name => "Test Protocol";
        public string Version => "1.0.0";
        
        public ProtocolMessage Parse(ReadOnlySpan<byte> rawData)
        {
            return new ProtocolMessage
            {
                ProtocolId = ProtocolId,
                Content = "Test",
                IsValid = true
            };
        }
    }
}
