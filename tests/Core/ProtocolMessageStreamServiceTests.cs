using ComCross.Core.Protocols;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using Xunit;

namespace ComCross.Tests.Core;

public class ProtocolMessageStreamServiceTests
{
    [Fact]
    public void AppendPhysicalFrame_NewSession_AddsFrame()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, // "Hello"
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Receive
        };
        
        // Act
        service.AppendPhysicalFrame("test-session", frame);
        var frames = service.GetPhysicalFrames("test-session");
        
        // Assert
        Assert.Single(frames);
        Assert.Equal(1, frames[0].FrameId);
    }
    
    [Fact]
    public void GetProtocolMessages_WithRawBytesProtocol_ReturnsHexString()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = new byte[] { 0xFF, 0xAB, 0x12 },
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Send
        };
        
        service.AppendPhysicalFrame("test-session", frame);
        
        // Act
        var messages = service.GetProtocolMessages("test-session", "raw-bytes");
        
        // Assert
        Assert.Single(messages);
        Assert.Equal("FF AB 12", messages[0].Content);
        Assert.True(messages[0].IsValid);
    }
    
    [Fact]
    public void GetProtocolMessages_WithAsciiProtocol_ReturnsDecodedText()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = System.Text.Encoding.ASCII.GetBytes("Hello World"),
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Receive
        };
        
        service.AppendPhysicalFrame("test-session", frame);
        
        // Act
        var messages = service.GetProtocolMessages("test-session", "ascii-text");
        
        // Assert
        Assert.Single(messages);
        Assert.Equal("Hello World", messages[0].Content);
        Assert.True(messages[0].IsValid);
    }
    
    [Fact]
    public void SetActiveProtocol_ChangesDefaultProtocol()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = new byte[] { 0x48, 0x65 },
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Receive
        };
        
        service.AppendPhysicalFrame("test-session", frame);
        
        // Act
        service.SetActiveProtocol("test-session", "raw-bytes");
        var activeProtocol = service.GetActiveProtocol("test-session");
        
        // Assert
        Assert.Equal("raw-bytes", activeProtocol);
        
        // 不指定协议ID时，应使用活动协议
        var messages = service.GetProtocolMessages("test-session", null);
        Assert.Single(messages);
        Assert.Equal("48 65", messages[0].Content);
    }
    
    [Fact]
    public void GetProtocolMessages_WithInvalidProtocol_ReturnsEmpty()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = new byte[] { 0x01, 0x02 },
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Send
        };
        
        service.AppendPhysicalFrame("test-session", frame);
        
        // Act
        var messages = service.GetProtocolMessages("test-session", "non-existing-protocol");
        
        // Assert
        Assert.Empty(messages);
    }
    
    [Fact]
    public void Clear_RemovesAllFrames()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = new byte[] { 0x01 },
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Receive
        };
        
        service.AppendPhysicalFrame("test-session", frame);
        
        // Act
        service.Clear("test-session");
        var frames = service.GetPhysicalFrames("test-session");
        
        // Assert
        Assert.Empty(frames);
    }
    
    [Fact]
    public void SubscribeToPhysicalFrames_NotifiesOnNewFrame()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        PhysicalFrame? receivedFrame = null;
        using var subscription = service.SubscribeToPhysicalFrames("test-session", frame =>
        {
            receivedFrame = frame;
        });
        
        var frame = new PhysicalFrame
        {
            FrameId = 100,
            SessionId = "test-session",
            Data = new byte[] { 0xAA },
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Send
        };
        
        // Act
        service.AppendPhysicalFrame("test-session", frame);
        
        // Assert
        Assert.NotNull(receivedFrame);
        Assert.Equal(100, receivedFrame.FrameId);
        Assert.Equal(new byte[] { 0xAA }, receivedFrame.Data.ToArray());
    }
    
    [Fact]
    public void SubscribeToProtocolMessages_NotifiesOnNewFrame()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        ProtocolMessage? receivedMessage = null;
        
        // 先订阅（这会创建会话流）
        using var subscription = service.SubscribeToProtocolMessages("test-session", "raw-bytes", message =>
        {
            receivedMessage = message;
        });
        
        // 然后设置活动协议
        service.SetActiveProtocol("test-session", "raw-bytes");
        
        var frame = new PhysicalFrame
        {
            FrameId = 1,
            SessionId = "test-session",
            Data = new byte[] { 0xDE, 0xAD },
            Timestamp = DateTime.UtcNow,
            Direction = MessageDirection.Receive
        };
        
        // Act
        service.AppendPhysicalFrame("test-session", frame);
        
        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("DE AD", receivedMessage.Content);
    }
    
    [Fact]
    public void GetPhysicalFrames_WithPagination_ReturnsCorrectRange()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        // 添加10个帧
        for (int i = 0; i < 10; i++)
        {
            service.AppendPhysicalFrame("test-session", new PhysicalFrame
            {
                FrameId = i,
                SessionId = "test-session",
                Data = new byte[] { (byte)i },
                Timestamp = DateTime.UtcNow,
                Direction = MessageDirection.Receive
            });
        }
        
        // Act
        var frames = service.GetPhysicalFrames("test-session", skip: 3, take: 4);
        
        // Assert
        Assert.Equal(4, frames.Count);
        Assert.Equal(3, frames[0].FrameId);
        Assert.Equal(6, frames[3].FrameId);
    }
    
    [Fact]
    public void GetProtocolMessages_WithPagination_ReturnsCorrectRange()
    {
        // Arrange
        var registry = new ProtocolRegistry();
        var cache = new ProtocolFrameCacheService();
        var service = new ProtocolMessageStreamService(registry, cache);
        
        // 添加5个帧
        for (int i = 0; i < 5; i++)
        {
            service.AppendPhysicalFrame("test-session", new PhysicalFrame
            {
                FrameId = i,
                SessionId = "test-session",
                Data = new byte[] { (byte)(0x10 + i) },
                Timestamp = DateTime.UtcNow,
                Direction = MessageDirection.Send
            });
        }
        
        // Act
        var messages = service.GetProtocolMessages("test-session", "raw-bytes", skip: 2, take: 2);
        
        // Assert
        Assert.Equal(2, messages.Count);
        Assert.Equal("12", messages[0].Content);
        Assert.Equal("13", messages[1].Content);
    }
}
