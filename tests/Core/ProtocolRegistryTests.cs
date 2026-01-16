using Xunit;
using ComCross.Shared.Interfaces;
using ComCross.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Tests.Core;

public class ProtocolRegistryTests
{
    private class TestParser : BaseMessageParser
    {
        public TestParser(bool isBuiltIn = false) 
            : base(NullLogger<TestParser>.Instance)
        {
            IsBuiltIn = isBuiltIn;
        }
        
        public override string Id => "test-protocol";
        public override string Name => "Test Protocol";
        public override string Version => "1.0.0";
        public override string Description => "Test protocol for unit testing";
        public override string Category => "Test";
        public override bool IsBuiltIn { get; }
        
        protected override ParseResult ParseCore(ReadOnlySpan<byte> data)
        {
            var fields = new Dictionary<string, object?> { ["length"] = data.Length };
            return ParseResult.Success(fields, data.ToArray());
        }
        
        protected override string FormatCore(ParseResult result, FormatOptions options)
        {
            return $"Length: {result.Fields["length"]}";
        }
    }
    
    [Fact]
    public void ProtocolRegistry_Register_AddsProtocol()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser();
        
        // Act
        registry.Register(parser);
        
        // Assert
        Assert.Equal(1, registry.Count);
        Assert.True(registry.IsRegistered("test-protocol"));
    }
    
    [Fact]
    public void ProtocolRegistry_Register_DuplicateId_ThrowsException()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser1 = new TestParser();
        var parser2 = new TestParser();
        
        // Act
        registry.Register(parser1);
        
        // Assert
        Assert.Throws<InvalidOperationException>(() => registry.Register(parser2));
    }
    
    [Fact]
    public void ProtocolRegistry_GetParser_ReturnsRegisteredParser()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser();
        registry.Register(parser);
        
        // Act
        var retrieved = registry.GetParser("test-protocol");
        
        // Assert
        Assert.Equal(parser.Id, retrieved.Id);
        Assert.Equal(parser.Name, retrieved.Name);
    }
    
    [Fact]
    public void ProtocolRegistry_GetParser_NotFound_ThrowsException()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        
        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => registry.GetParser("non-existent"));
    }
    
    [Fact]
    public void ProtocolRegistry_TryGetParser_Success()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser();
        registry.Register(parser);
        
        // Act
        var found = registry.TryGetParser("test-protocol", out var retrieved);
        
        // Assert
        Assert.True(found);
        Assert.NotNull(retrieved);
        Assert.Equal("test-protocol", retrieved.Id);
    }
    
    [Fact]
    public void ProtocolRegistry_TryGetParser_NotFound_ReturnsFalse()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        
        // Act
        var found = registry.TryGetParser("non-existent", out var retrieved);
        
        // Assert
        Assert.False(found);
        Assert.Null(retrieved);
    }
    
    [Fact]
    public void ProtocolRegistry_Unregister_CustomProtocol_Success()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser(isBuiltIn: false);
        registry.Register(parser);
        
        // Act
        var result = registry.Unregister("test-protocol");
        
        // Assert
        Assert.True(result);
        Assert.Equal(0, registry.Count);
        Assert.False(registry.IsRegistered("test-protocol"));
    }
    
    [Fact]
    public void ProtocolRegistry_Unregister_BuiltInProtocol_ThrowsException()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser(isBuiltIn: true);
        registry.Register(parser);
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => registry.Unregister("test-protocol"));
    }
    
    [Fact]
    public void ProtocolRegistry_Unregister_NotFound_ReturnsFalse()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        
        // Act
        var result = registry.Unregister("non-existent");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void ProtocolRegistry_GetAll_ReturnsAllProtocols()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser1 = new TestParser(isBuiltIn: true);
        var parser2 = new CustomParser(isBuiltIn: false);
        registry.Register(parser1);
        registry.Register(parser2);
        
        // Act
        var all = registry.GetAll();
        
        // Assert
        Assert.Equal(2, all.Count);
    }
    
    [Fact]
    public void ProtocolRegistry_GetBuiltIn_ReturnsOnlyBuiltIn()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser1 = new TestParser(isBuiltIn: true);
        var parser2 = new CustomParser(isBuiltIn: false);
        registry.Register(parser1);
        registry.Register(parser2);
        
        // Act
        var builtIn = registry.GetBuiltIn();
        
        // Assert
        Assert.Single(builtIn);
        Assert.True(builtIn[0].IsBuiltIn);
    }
    
    [Fact]
    public void ProtocolRegistry_GetCustom_ReturnsOnlyCustom()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser1 = new TestParser(isBuiltIn: true);
        var parser2 = new CustomParser(isBuiltIn: false);
        registry.Register(parser1);
        registry.Register(parser2);
        
        // Act
        var custom = registry.GetCustom();
        
        // Assert
        Assert.Single(custom);
        Assert.False(custom[0].IsBuiltIn);
    }
    
    [Fact]
    public void ProtocolRegistry_RegisterRange_AddsMultipleProtocols()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parsers = new IMessageParser[] { new TestParser(), new CustomParser() };
        
        // Act
        registry.RegisterRange(parsers);
        
        // Assert
        Assert.Equal(2, registry.Count);
    }
    
    [Fact]
    public void ProtocolRegistry_ProtocolRegistered_EventRaised()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser();
        bool eventRaised = false;
        string? protocolId = null;
        
        registry.ProtocolRegistered += (sender, e) =>
        {
            eventRaised = true;
            protocolId = e.ProtocolId;
        };
        
        // Act
        registry.Register(parser);
        
        // Assert
        Assert.True(eventRaised);
        Assert.Equal("test-protocol", protocolId);
    }
    
    [Fact]
    public void ProtocolRegistry_ProtocolUnregistered_EventRaised()
    {
        // Arrange
        var registry = new ProtocolRegistry(NullLogger<ProtocolRegistry>.Instance);
        var parser = new TestParser(isBuiltIn: false);
        registry.Register(parser);
        
        bool eventRaised = false;
        string? protocolId = null;
        
        registry.ProtocolUnregistered += (sender, e) =>
        {
            eventRaised = true;
            protocolId = e.ProtocolId;
        };
        
        // Act
        registry.Unregister("test-protocol");
        
        // Assert
        Assert.True(eventRaised);
        Assert.Equal("test-protocol", protocolId);
    }
    
    // Helper class for testing
    private class CustomParser : BaseMessageParser
    {
        public CustomParser(bool isBuiltIn = false) 
            : base(NullLogger<CustomParser>.Instance)
        {
            IsBuiltIn = isBuiltIn;
        }
        
        public override string Id => "custom-protocol";
        public override string Name => "Custom Protocol";
        public override string Version => "1.0.0";
        public override string Description => "Custom test protocol";
        public override string Category => "Custom";
        public override bool IsBuiltIn { get; }
        
        protected override ParseResult ParseCore(ReadOnlySpan<byte> data)
        {
            var fields = new Dictionary<string, object?> { ["custom"] = "data" };
            return ParseResult.Success(fields, data.ToArray());
        }
        
        protected override string FormatCore(ParseResult result, FormatOptions options)
        {
            return "Custom Format";
        }
    }
}
