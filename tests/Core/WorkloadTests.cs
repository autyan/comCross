using Xunit;
using ComCross.Core.Models;

namespace ComCross.Tests.Core;

public class WorkloadTests
{
    [Fact]
    public void Workload_Create_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var workload = Workload.Create("Test Workload", isDefault: false);
        
        // Assert
        Assert.NotNull(workload.Id);
        Assert.Equal("Test Workload", workload.Name);
        Assert.False(workload.IsDefault);
        Assert.NotEqual(default, workload.CreatedAt);
        Assert.NotEqual(default, workload.UpdatedAt);
        Assert.Empty(workload.SessionIds);
    }
    
    [Fact]
    public void Workload_CreateDefault_SetsIsDefaultTrue()
    {
        // Arrange & Act
        var workload = Workload.Create("Default", isDefault: true);
        
        // Assert
        Assert.True(workload.IsDefault);
    }
    
    [Fact]
    public void Workload_AddSession_AddsToList()
    {
        // Arrange
        var workload = Workload.Create("Test");
        var sessionId = "session-123";
        var initialUpdateTime = workload.UpdatedAt;
        
        // Wait a tiny bit to ensure time difference
        Thread.Sleep(10);
        
        // Act
        workload.AddSession(sessionId);
        
        // Assert
        Assert.Contains(sessionId, workload.SessionIds);
        Assert.True(workload.UpdatedAt > initialUpdateTime);
    }
    
    [Fact]
    public void Workload_AddSession_PreventsDuplicates()
    {
        // Arrange
        var workload = Workload.Create("Test");
        var sessionId = "session-123";
        
        // Act
        workload.AddSession(sessionId);
        workload.AddSession(sessionId); // Add same session again
        
        // Assert
        Assert.Single(workload.SessionIds);
    }
    
    [Fact]
    public void Workload_RemoveSession_RemovesFromList()
    {
        // Arrange
        var workload = Workload.Create("Test");
        var sessionId = "session-123";
        workload.AddSession(sessionId);
        
        // Act
        var result = workload.RemoveSession(sessionId);
        
        // Assert
        Assert.True(result);
        Assert.DoesNotContain(sessionId, workload.SessionIds);
    }
    
    [Fact]
    public void Workload_RemoveSession_ReturnsFalseIfNotFound()
    {
        // Arrange
        var workload = Workload.Create("Test");
        
        // Act
        var result = workload.RemoveSession("nonexistent-session");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void Workload_Rename_UpdatesNameAndTime()
    {
        // Arrange
        var workload = Workload.Create("Old Name");
        var initialUpdateTime = workload.UpdatedAt;
        
        Thread.Sleep(10);
        
        // Act
        workload.Rename("New Name");
        
        // Assert
        Assert.Equal("New Name", workload.Name);
        Assert.True(workload.UpdatedAt > initialUpdateTime);
    }
    
    [Fact]
    public void Workload_AddMultipleSessions_MaintainsList()
    {
        // Arrange
        var workload = Workload.Create("Test");
        var sessions = new[] { "session-1", "session-2", "session-3" };
        
        // Act
        foreach (var sessionId in sessions)
        {
            workload.AddSession(sessionId);
        }
        
        // Assert
        Assert.Equal(3, workload.SessionIds.Count);
        foreach (var sessionId in sessions)
        {
            Assert.Contains(sessionId, workload.SessionIds);
        }
    }
}
