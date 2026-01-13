using Xunit;
using ComCross.Core.Services;
using ComCross.Shared.Models;

namespace ComCross.Tests.Core;

public class EventBusTests
{
    [Fact]
    public void EventBus_PublishSubscribe_WorksCorrectly()
    {
        // Arrange
        var eventBus = new EventBus();
        var received = false;
        var message = "test";

        // Act
        eventBus.Subscribe<TestEvent>(e =>
        {
            received = true;
            Assert.Equal(message, e.Message);
        });

        eventBus.Publish(new TestEvent(message));

        // Assert
        Assert.True(received);
    }

    [Fact]
    public void EventBus_Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        var eventBus = new EventBus();
        var count = 0;

        // Act
        var subscription = eventBus.Subscribe<TestEvent>(e => count++);
        eventBus.Publish(new TestEvent("test1"));
        
        subscription.Dispose();
        
        eventBus.Publish(new TestEvent("test2"));

        // Assert
        Assert.Equal(1, count);
    }

    private record TestEvent(string Message);
}
