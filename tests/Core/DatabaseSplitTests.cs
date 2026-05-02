using System;
using System.IO;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Core.Models;
using Xunit;

namespace ComCross.Tests.Core;

/// <summary>
/// Tests for database split architecture (MainDatabaseService + WorkspaceDatabaseService).
/// </summary>
public sealed class DatabaseSplitTests : IDisposable
{
    private readonly string _testDirectory;
    private DatabaseManager? _dbManager;

    public DatabaseSplitTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ComCrossTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task DatabaseManager_Initialize_CreatesBothDatabases()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);

        // Act
        await _dbManager.InitializeAsync();

        // Assert
        Assert.True(_dbManager.IsInitialized);
        Assert.True(File.Exists(Path.Combine(_testDirectory, "comcross.db")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "workspace.db")));
    }

    [Fact]
    public async Task DatabaseManager_HealthCheck_ReturnsHealthy()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        // Act
        var health = await _dbManager.CheckHealthAsync();

        // Assert
        Assert.True(health.IsHealthy);
        Assert.Null(health.Error);
        Assert.NotNull(health.MainDatabasePath);
        Assert.NotNull(health.WorkspaceDatabasePath);
    }

    [Fact]
    public async Task MainDatabase_ConfigOperations_WorkCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        // Act
        await _dbManager.MainDb.SetConfigAsync("test_key", "test_value");
        var value = await _dbManager.MainDb.GetConfigAsync("test_key");

        // Assert
        Assert.Equal("test_value", value);
    }

    [Fact]
    public async Task MainDatabase_PreferenceOperations_WorkCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        // Act
        await _dbManager.MainDb.SetPreferenceAsync("ui_theme", "dark");
        var value = await _dbManager.MainDb.GetPreferenceAsync("ui_theme");

        // Assert
        Assert.Equal("dark", value);
    }

    [Fact]
    public async Task WorkspaceDatabase_WorkloadOperations_WorkCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        var workload = new ComCross.Core.Models.Workload
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Workload",
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        var workloads = await _dbManager.WorkspaceDb.GetAllWorkloadsAsync();

        // Assert
        Assert.Single(workloads);
        Assert.Equal("Test Workload", workloads[0].Name);
    }

    [Fact]
    public async Task WorkspaceDatabase_MessageOperations_WorkCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        var workloadId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rawData = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Create workload and session first
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        // Act
        await _dbManager.WorkspaceDb.InsertMessageAsync(sessionId, timestamp, "RX", rawData);
        var messages = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: 10);

        // Assert
        Assert.Single(messages);
        Assert.Equal(timestamp, messages[0].Timestamp);
        Assert.Equal("RX", messages[0].Direction);
        Assert.Equal(rawData, messages[0].RawData);
    }

    [Fact]
    public async Task WorkspaceDatabase_BatchInsert_WorksCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        var workloadId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();

        // Create workload and session first
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        var messages = new List<(string SessionId, long Timestamp, string Direction, byte[] RawData)>();

        for (int i = 0; i < 100; i++)
        {
            messages.Add((
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i,
                i % 2 == 0 ? "RX" : "TX",
                new byte[] { (byte)i }
            ));
        }

        // Act
        await _dbManager.WorkspaceDb.InsertMessageBatchAsync(messages);
        var retrieved = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: 100);

        // Assert
        Assert.Equal(100, retrieved.Count);
    }

    [Fact]
    public async Task WorkspaceDatabase_UpdateWorkload_WorksCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        var workload = new ComCross.Core.Models.Workload
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Original Name",
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);

        // Act
        workload.Name = "Updated Name";
        workload.Description = "New Description";
        workload.UpdatedAt = DateTime.UtcNow;
        await _dbManager.WorkspaceDb.UpdateWorkloadAsync(workload);

        var workloads = await _dbManager.WorkspaceDb.GetAllWorkloadsAsync();

        // Assert
        Assert.Single(workloads);
        Assert.Equal("Updated Name", workloads[0].Name);
        Assert.Equal("New Description", workloads[0].Description);
    }

    [Fact]
    public async Task WorkspaceDatabase_DeleteWorkload_WorksCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        var workload = new ComCross.Core.Models.Workload
        {
            Id = Guid.NewGuid().ToString(),
            Name = "To Delete",
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);

        // Act
        await _dbManager.WorkspaceDb.DeleteWorkloadAsync(workload.Id);
        var workloads = await _dbManager.WorkspaceDb.GetAllWorkloadsAsync();

        // Assert
        Assert.Empty(workloads);
    }

    [Fact]
    public async Task DatabaseManager_Close_DisposesConnections()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();

        // Act
        await _dbManager.CloseAsync();

        // Assert
        Assert.False(_dbManager.IsInitialized);
        Assert.Throws<InvalidOperationException>(() => _dbManager.MainDb);
        Assert.Throws<InvalidOperationException>(() => _dbManager.WorkspaceDb);
    }

    public void Dispose()
    {
        _dbManager?.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
