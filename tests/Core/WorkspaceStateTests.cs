using Xunit;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Tests.Core;

public class WorkspaceStateTests
{
    [Fact]
    public void WorkspaceState_GetDefaultWorkload_ReturnsDefault()
    {
        // Arrange
        var state = new WorkspaceState();
        var defaultWorkload = Workload.Create("Default", isDefault: true);
        var otherWorkload = Workload.Create("Other", isDefault: false);
        state.Workloads.Add(defaultWorkload);
        state.Workloads.Add(otherWorkload);
        
        // Act
        var result = state.GetDefaultWorkload();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(defaultWorkload.Id, result.Id);
        Assert.True(result.IsDefault);
    }
    
    [Fact]
    public void WorkspaceState_GetDefaultWorkload_ReturnsNullIfNoneExists()
    {
        // Arrange
        var state = new WorkspaceState();
        state.Workloads.Add(Workload.Create("Non-default", isDefault: false));
        
        // Act
        var result = state.GetDefaultWorkload();
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void WorkspaceState_EnsureDefaultWorkload_CreatesIfNotExists()
    {
        // Arrange
        var state = new WorkspaceState();
        Assert.Empty(state.Workloads);
        
        // Act
        state.EnsureDefaultWorkload();
        
        // Assert
        Assert.Single(state.Workloads);
        var workload = state.Workloads[0];
        Assert.True(workload.IsDefault);
        Assert.Equal("默认任务", workload.Name);
    }
    
    [Fact]
    public void WorkspaceState_EnsureDefaultWorkload_DoesNotDuplicateIfExists()
    {
        // Arrange
        var state = new WorkspaceState();
        var defaultWorkload = Workload.Create("Existing Default", isDefault: true);
        state.Workloads.Add(defaultWorkload);
        
        // Act
        state.EnsureDefaultWorkload();
        
        // Assert
        Assert.Single(state.Workloads);
        Assert.Equal("Existing Default", state.Workloads[0].Name);
    }
    
    [Fact]
    public void WorkspaceState_Version_DefaultsTo040()
    {
        // Arrange & Act
        var state = new WorkspaceState();
        
        // Assert
        Assert.Equal("0.4.0", state.Version);
    }
}

public class WorkspaceMigrationTests
{
    [Fact]
    public void Migration_FromOldVersion_CreatesEmptyDefaultWorkload()
    {
        // Arrange
        var migration = new WorkspaceMigrationService(NullLogger<WorkspaceMigrationService>.Instance);
        var oldState = new WorkspaceState
        {
            Version = "0.3.2"
        };
        
        // Act
        var newState = migration.Migrate(oldState);
        
        // Assert
        Assert.Single(newState.Workloads);
        var workload = newState.Workloads[0];
        Assert.True(workload.IsDefault);
        Assert.Empty(workload.SessionIds);
    }
    
    [Fact]
    public void Migration_NeedsMigration_DetectsVersionDifference()
    {
        // Arrange
        var migration = new WorkspaceMigrationService(NullLogger<WorkspaceMigrationService>.Instance);
        var oldState = new WorkspaceState { Version = "0.3.2" };
        var newState = new WorkspaceState { Version = "0.4.0" };
        
        // Act & Assert
        Assert.True(migration.NeedsMigration(oldState));
        Assert.False(migration.NeedsMigration(newState));
    }
    
    [Fact]
    public void Migration_NeedsMigration_TreatsNullVersionAs03()
    {
        // Arrange
        var migration = new WorkspaceMigrationService(NullLogger<WorkspaceMigrationService>.Instance);
        var oldState = new WorkspaceState { Version = null! };
        
        // Act & Assert
        Assert.True(migration.NeedsMigration(oldState));
    }
    
    [Fact]
    public void Migration_PreservesExistingWorkloads()
    {
        // Arrange
        var migration = new WorkspaceMigrationService(NullLogger<WorkspaceMigrationService>.Instance);
        var existingWorkload = Workload.Create("Existing", isDefault: true);
        var oldState = new WorkspaceState
        {
            Version = "0.3.2",
            Workloads = new List<Workload> { existingWorkload }
        };
        
        // Act
        var newState = migration.Migrate(oldState);
        
        // Assert
        Assert.Single(newState.Workloads);
        Assert.Equal(existingWorkload.Id, newState.Workloads[0].Id);
        Assert.Empty(newState.Workloads[0].SessionIds);
    }
}
