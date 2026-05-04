using ComCross.Core.Services;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class MessageFrameQueryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "comcross-query-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void QueryLatest_ReturnsLatestLiveSpoolFrames()
    {
        var store = CreateStore(maxPerSessionSizeMb: 16);
        var query = new MessageFrameQueryService(store, NullLogger<MessageFrameQueryService>.Instance);
        AppendFrames(store, "session-latest", count: 5, payloadBytes: 1);

        var result = query.Query(new MessageFrameQuery(
            "session-latest",
            MessageFrameDataSource.LiveSpool,
            MessageFrameQueryKind.Latest,
            FrameId: 0,
            Limit: 2));

        Assert.Equal(MessageFrameQueryStatus.Ok, result.Status);
        Assert.Equal(new long[] { 4, 5 }, result.Frames.Select(frame => frame.FrameId));
        Assert.Equal(1, result.FirstAvailableFrameId);
        Assert.Equal(5, result.LastAvailableFrameId);
    }

    [Fact]
    public void QueryAfter_ReturnsDataEvictedWithAvailableFrames()
    {
        var store = CreateStore(maxPerSessionSizeMb: 1);
        var query = new MessageFrameQueryService(store, NullLogger<MessageFrameQueryService>.Instance);
        AppendFrames(store, "session-evicted", count: 3, payloadBytes: 800 * 1024);

        var result = query.Query(new MessageFrameQuery(
            "session-evicted",
            MessageFrameDataSource.LiveSpool,
            MessageFrameQueryKind.After,
            FrameId: 0,
            Limit: 10));

        Assert.Equal(MessageFrameQueryStatus.DataEvicted, result.Status);
        Assert.True(result.FirstAvailableFrameId > 1);
        Assert.All(result.Frames, frame => Assert.True(frame.FrameId >= result.FirstAvailableFrameId));
    }

    [Fact]
    public void QueryBefore_ReturnsWindowBeforeFrameId()
    {
        var store = CreateStore(maxPerSessionSizeMb: 16);
        var query = new MessageFrameQueryService(store, NullLogger<MessageFrameQueryService>.Instance);
        AppendFrames(store, "session-before", count: 5, payloadBytes: 1);

        var result = query.Query(new MessageFrameQuery(
            "session-before",
            MessageFrameDataSource.LiveSpool,
            MessageFrameQueryKind.Before,
            FrameId: 5,
            Limit: 2));

        Assert.Equal(MessageFrameQueryStatus.Ok, result.Status);
        Assert.Equal(new long[] { 3, 4 }, result.Frames.Select(frame => frame.FrameId));
    }

    [Fact]
    public void QueryArchive_ReturnsArchiveDisabledInStageThree()
    {
        var store = CreateStore(maxPerSessionSizeMb: 16);
        var query = new MessageFrameQueryService(store, NullLogger<MessageFrameQueryService>.Instance);

        var result = query.Query(new MessageFrameQuery(
            "session-archive",
            MessageFrameDataSource.Archive,
            MessageFrameQueryKind.Latest,
            FrameId: 0,
            Limit: 10));

        Assert.Equal(MessageFrameQueryStatus.ArchiveDisabled, result.Status);
        Assert.Empty(result.Frames);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private SessionSpoolFrameStore CreateStore(int maxPerSessionSizeMb)
    {
        var paths = new ComCrossPathService(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "config"),
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"));
        var settings = new SettingsService(new ConfigService(paths), new AppDatabase(paths), paths);
        settings.Current.Logs.MaxFileSizeMb = 1;
        settings.Current.Logs.MaxPerSessionSizeMb = maxPerSessionSizeMb;
        settings.Current.Logs.MaxTotalSizeMb = 64;
        var notification = new NotificationService(new AppDatabase(paths), settings);
        var health = new StorageHealthService(notification, NullLogger<StorageHealthService>.Instance);
        var policy = new StoragePolicyService(health);
        return new SessionSpoolFrameStore(paths, settings, policy, health, NullLogger<SessionSpoolFrameStore>.Instance);
    }

    private static void AppendFrames(SessionSpoolFrameStore store, string sessionId, int count, int payloadBytes)
    {
        var payload = new byte[payloadBytes];
        for (var i = 0; i < count; i++)
        {
            payload[0] = (byte)i;
            store.Append(sessionId, DateTime.UtcNow.AddMilliseconds(i), FrameDirection.Rx, payload, MessageFormat.Hex, "test");
        }
    }
}
