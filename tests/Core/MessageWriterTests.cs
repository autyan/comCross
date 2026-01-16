using System;
using System.IO;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Core.Models;
using Xunit;

namespace ComCross.Tests.Core;

/// <summary>
/// Tests for MessageWriter (Channel-based batch writing).
/// </summary>
public sealed class MessageWriterTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private DatabaseManager? _dbManager;
    private MessageWriter? _writer;

    public MessageWriterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ComCrossTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task MessageWriter_WriteAsync_InsertsMessages()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 10, batchTimeoutMs: 100);

        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var sessionId = Guid.NewGuid().ToString();

        // Create workload and session first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await _writer.WriteAsync(
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                "RX",
                new byte[] { (byte)i });
        }

        // Wait for batch write
        await Task.Delay(200);

        // Assert
        var messages = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: 10);
        Assert.Equal(5, messages.Count);
    }

    [Fact]
    public async Task MessageWriter_BatchSize_TriggersBatchWrite()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 10, batchTimeoutMs: 5000);

        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var sessionId = Guid.NewGuid().ToString();

        // Create workload and session first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        // Act: Write exactly batch size (should trigger immediate write)
        for (int i = 0; i < 10; i++)
        {
            await _writer.WriteAsync(
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i,
                "TX",
                new byte[] { (byte)i });
        }

        // Small delay to allow async write to complete
        await Task.Delay(100);

        // Assert
        var messages = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: 20);
        Assert.Equal(10, messages.Count);

        var stats = _writer.GetStatistics();
        Assert.Equal(10, stats.TotalMessagesWritten);
        Assert.True(stats.TotalBatchesWritten >= 1);
    }

    [Fact]
    public async Task MessageWriter_Timeout_TriggersBatchWrite()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 100, batchTimeoutMs: 100);

        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var sessionId = Guid.NewGuid().ToString();

        // Create workload and session first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        // Act: Write less than batch size (should write after timeout)
        for (int i = 0; i < 5; i++)
        {
            await _writer.WriteAsync(
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                "RX",
                new byte[] { (byte)i });
        }

        // Wait for timeout
        await Task.Delay(200);

        // Assert
        var messages = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: 10);
        Assert.Equal(5, messages.Count);
    }

    [Fact]
    public async Task MessageWriter_HighVolume_WritesAllMessages()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 100, batchTimeoutMs: 100);

        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var sessionId = Guid.NewGuid().ToString();
        const int messageCount = 1000;

        // Create workload and session first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        // Act: Write large number of messages
        for (int i = 0; i < messageCount; i++)
        {
            await _writer.WriteAsync(
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i,
                i % 2 == 0 ? "RX" : "TX",
                BitConverter.GetBytes(i));
        }

        // Wait for all batches to complete
        await Task.Delay(500);

        // Assert
        var stats = _writer.GetStatistics();
        Assert.Equal(messageCount, stats.TotalMessagesWritten);

        // Verify messages in database
        var messages = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: messageCount);
        Assert.Equal(messageCount, messages.Count);
    }

    [Fact]
    public async Task MessageWriter_Statistics_TrackCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 10, batchTimeoutMs: 100);

        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var sessionId = Guid.NewGuid().ToString();

        // Create workload and session first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);

        // Act: Write 25 messages (should result in 3 batches: 10, 10, 5)
        for (int i = 0; i < 25; i++)
        {
            await _writer.WriteAsync(
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                "RX",
                new byte[] { (byte)i });
        }

        await Task.Delay(200);

        // Assert
        var stats = _writer.GetStatistics();
        Assert.Equal(25, stats.TotalMessagesWritten);
        Assert.True(stats.TotalBatchesWritten >= 2); // At least 2 batches
        Assert.True(stats.AverageBatchSize > 0);
    }

    [Fact]
    public async Task MessageWriter_MultipleSession_WritesCorrectly()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 10, batchTimeoutMs: 100);

        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var session1 = Guid.NewGuid().ToString();
        var session2 = Guid.NewGuid().ToString();

        // Create workload and sessions first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(session1, workloadId, "Test Session 1", "COM1", 9600);
        await _dbManager.WorkspaceDb.InsertSessionAsync(session2, workloadId, "Test Session 2", "COM2", 9600);

        // Act: Write to multiple sessions
        for (int i = 0; i < 10; i++)
        {
            await _writer.WriteAsync(session1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "RX", new byte[] { 1 });
            await _writer.WriteAsync(session2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "TX", new byte[] { 2 });
        }

        await Task.Delay(200);

        // Assert
        var messages1 = await _dbManager.WorkspaceDb.GetMessagesAsync(session1, limit: 20);
        var messages2 = await _dbManager.WorkspaceDb.GetMessagesAsync(session2, limit: 20);

        Assert.Equal(10, messages1.Count);
        Assert.Equal(10, messages2.Count);
        Assert.All(messages1, m => Assert.Equal("RX", m.Direction));
        Assert.All(messages2, m => Assert.Equal("TX", m.Direction));
    }

    [Fact]
    public async Task MessageWriter_Dispose_FlushesRemainingMessages()
    {
        // Arrange
        _dbManager = new DatabaseManager(_testDirectory);
        await _dbManager.InitializeAsync();
        
        var workloadId = Guid.NewGuid().ToString();
        var workload = Workload.Create("Test Workload");
        workload.Id = workloadId;
        var sessionId = Guid.NewGuid().ToString();

        // Create workload and session first
        await _dbManager.WorkspaceDb.InsertWorkloadAsync(workload);
        await _dbManager.WorkspaceDb.InsertSessionAsync(sessionId, workloadId, "Test Session", "COM1", 9600);
        
        _writer = new MessageWriter(_dbManager.WorkspaceDb, batchSize: 100, batchTimeoutMs: 5000);

        // Act: Write messages but don't wait for timeout
        for (int i = 0; i < 5; i++)
        {
            await _writer.WriteAsync(
                sessionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                "RX",
                new byte[] { (byte)i });
        }

        // Dispose immediately (should flush remaining messages)
        await _writer.DisposeAsync();

        // Assert
        var messages = await _dbManager.WorkspaceDb.GetMessagesAsync(sessionId, limit: 10);
        Assert.Equal(5, messages.Count);
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer != null)
        {
            await _writer.DisposeAsync();
        }

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
