using ComCross.Core.Services;
using ComCross.Shared.Events;
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
        var query = CreateQueryService(store);
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
        var query = CreateQueryService(store);
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
        var query = CreateQueryService(store);
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
    public void QueryArchive_ReturnsArchiveDisabledWhenSessionArchiveIsDisabled()
    {
        var store = CreateStore(maxPerSessionSizeMb: 16);
        var query = CreateQueryService(store);

        var result = query.Query(new MessageFrameQuery(
            "session-archive",
            MessageFrameDataSource.Archive,
            MessageFrameQueryKind.Latest,
            FrameId: 0,
            Limit: 10));

        Assert.Equal(MessageFrameQueryStatus.ArchiveDisabled, result.Status);
        Assert.Empty(result.Frames);
    }

    [Fact]
    public void QueryArchive_ReturnsArchivedFrames()
    {
        var store = CreateStore(maxPerSessionSizeMb: 16);
        var archiveStore = CreateArchiveStore();
        var eventBus = new EventBus();
        var tracker = new SessionArchiveStateTracker(eventBus);
        eventBus.Publish(new SessionCreatedEvent(new Session
        {
            Id = "session-archive",
            Name = "Archive Session",
            ArchiveState = SessionArchiveState.Enabled
        }));
        archiveStore.Append(new MessageFrameRecord(1, "session-archive", DateTime.UtcNow, FrameDirection.Rx, [0x01], MessageFormat.Hex, "test"));
        archiveStore.Append(new MessageFrameRecord(2, "session-archive", DateTime.UtcNow, FrameDirection.Tx, [0x02], MessageFormat.Hex, "test"));
        var query = new MessageFrameQueryService(store, archiveStore, tracker, NullLogger<MessageFrameQueryService>.Instance);

        var result = query.Query(new MessageFrameQuery(
            "session-archive",
            MessageFrameDataSource.Archive,
            MessageFrameQueryKind.Latest,
            FrameId: 0,
            Limit: 10));

        Assert.Equal(MessageFrameQueryStatus.Ok, result.Status);
        Assert.Equal(new long[] { 1, 2 }, result.Frames.Select(frame => frame.FrameId));
        Assert.Equal(1, result.FirstAvailableFrameId);
        Assert.Equal(2, result.LastAvailableFrameId);
    }

    [Fact]
    public async Task ArchivingFrameStore_WritesOnlyFramesAppendedAfterArchiveIsEnabled()
    {
        var inner = CreateStore(maxPerSessionSizeMb: 16);
        var archiveStore = CreateArchiveStore();
        var eventBus = new EventBus();
        var tracker = new SessionArchiveStateTracker(eventBus);
        var paths = CreatePaths();
        var settings = new SettingsService(new ConfigService(paths), new AppDatabase(paths), paths);
        var database = new AppDatabase(paths);
        await database.InitializeAsync();
        var notification = new NotificationService(database, settings);
        var health = new StorageHealthService(notification, NullLogger<StorageHealthService>.Instance);
        using var writer = new SessionArchiveWriter(
            archiveStore,
            tracker,
            notification,
            health,
            eventBus,
            NullLogger<SessionArchiveWriter>.Instance);
        var store = new ArchivingFrameStore(inner, writer);

        eventBus.Publish(new SessionCreatedEvent(new Session
        {
            Id = "session-write",
            Name = "Archive Write Session",
            ArchiveState = SessionArchiveState.Disabled
        }));
        store.Append("session-write", DateTime.UtcNow, FrameDirection.Rx, [0x01], MessageFormat.Hex, "test");

        eventBus.Publish(new SessionUpdatedEvent(new Session
        {
            Id = "session-write",
            Name = "Archive Write Session",
            ArchiveState = SessionArchiveState.Enabled
        }));
        store.Append("session-write", DateTime.UtcNow, FrameDirection.Rx, [0x02], MessageFormat.Hex, "test");

        await WaitForAsync(() => archiveStore.GetWindowInfo("session-write").LastFrameId == 2);

        var frames = archiveStore.ReadLatest("session-write", 10);
        var frame = Assert.Single(frames);
        Assert.Equal(2, frame.FrameId);
        Assert.Equal(new byte[] { 0x02 }, frame.RawData);
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
        settings.Current.SessionStorage.SegmentSizeLimitMb = 1;
        settings.Current.SessionStorage.PerSessionSizeLimitMb = maxPerSessionSizeMb;
        settings.Current.SessionStorage.GlobalSizeLimitMb = 64;
        var notification = new NotificationService(new AppDatabase(paths), settings);
        var health = new StorageHealthService(notification, NullLogger<StorageHealthService>.Instance);
        var policy = new StoragePolicyService(health);
        return new SessionSpoolFrameStore(paths, settings, policy, health, NullLogger<SessionSpoolFrameStore>.Instance);
    }

    private MessageFrameQueryService CreateQueryService(SessionSpoolFrameStore store)
    {
        var eventBus = new EventBus();
        return new MessageFrameQueryService(
            store,
            CreateArchiveStore(),
            new SessionArchiveStateTracker(eventBus),
            NullLogger<MessageFrameQueryService>.Instance);
    }

    private SessionArchiveStore CreateArchiveStore()
    {
        return new SessionArchiveStore(CreatePaths(), NullLogger<SessionArchiveStore>.Instance);
    }

    private ComCrossPathService CreatePaths()
        => new(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "config"),
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition());
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
