using Xunit;
using ComCross.Core.Services;
using ComCross.Shared.Models;

namespace ComCross.Tests.Core;

public class MessageStreamServiceTests
{
    [Fact]
    public void MessageStream_Append_AddsMessage()
    {
        // Arrange
        var service = new MessageStreamService();
        var sessionId = "test-session";
        var message = new LogMessage
        {
            Id = "1",
            Timestamp = DateTime.UtcNow,
            Content = "Test message",
            Level = LogLevel.Info
        };

        // Act
        service.Append(sessionId, message);
        var messages = service.GetMessages(sessionId);

        // Assert
        Assert.Single(messages);
        Assert.Equal("Test message", messages[0].Content);
    }

    [Fact]
    public void MessageStream_Search_FindsMatches()
    {
        // Arrange
        var service = new MessageStreamService();
        var sessionId = "test-session";
        
        service.Append(sessionId, new LogMessage
        {
            Id = "1",
            Timestamp = DateTime.UtcNow,
            Content = "Error occurred",
            Level = LogLevel.Error
        });
        
        service.Append(sessionId, new LogMessage
        {
            Id = "2",
            Timestamp = DateTime.UtcNow,
            Content = "Info message",
            Level = LogLevel.Info
        });

        // Act
        var results = service.Search(sessionId, "Error");

        // Assert
        Assert.Single(results);
        Assert.Contains("Error", results[0].Content);
    }

    [Fact]
    public void MessageStream_Clear_RemovesAllMessages()
    {
        // Arrange
        var service = new MessageStreamService();
        var sessionId = "test-session";
        
        service.Append(sessionId, new LogMessage
        {
            Id = "1",
            Timestamp = DateTime.UtcNow,
            Content = "Test",
            Level = LogLevel.Info
        });

        // Act
        service.Clear(sessionId);
        var messages = service.GetMessages(sessionId);

        // Assert
        Assert.Empty(messages);
    }
}
