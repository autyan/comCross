using System.Text.Json;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class SessionLogExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "comcross-export-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_WritesCompleteLiveSpoolPlainCclog()
    {
        var services = await CreateServicesAsync();
        var session = new Session { Id = "session-plain", Name = "Plain Session" };
        services.Store.Append(session.Id, DateTime.UtcNow, FrameDirection.Rx, "hello\r\n"u8.ToArray(), MessageFormat.Text, "test");
        services.Store.Append(session.Id, DateTime.UtcNow, FrameDirection.Tx, [0x01, 0x02], MessageFormat.Hex, "test");

        var path = await services.Export.ExportAsync(session);
        var text = await File.ReadAllTextAsync(path);

        Assert.EndsWith(".cclog", path);
        Assert.Contains("CCLOG/1", text);
        Assert.Contains("format: plain", text);
        Assert.Contains("source: LiveSpool", text);
        Assert.Contains("result: complete", text);
        Assert.Contains("firstFrameId: 1", text);
        Assert.Contains("lastFrameId: 2", text);
        Assert.Contains("exportedFrames: 2", text);
        Assert.Contains("hello\\r\\n", text);
        Assert.DoesNotContain("RX\t", text);
    }

    [Fact]
    public async Task ExportAsync_WritesSlimDirectionAndHexPayload()
    {
        var services = await CreateServicesAsync();
        services.Settings.Current.Export.DefaultSessionLogFormat = SessionLogExportFormat.Slim;
        services.Settings.Current.Export.DefaultPayloadRenderMode = PayloadRenderMode.Hex;
        var session = new Session { Id = "session-slim", Name = "Slim Session" };
        services.Store.Append(session.Id, DateTime.UtcNow, FrameDirection.Rx, [0xAA, 0x55], MessageFormat.Hex, "udp");
        services.Store.Append(session.Id, DateTime.UtcNow, FrameDirection.Tx, [0x0D, 0x0A], MessageFormat.Hex, "udp");

        var path = await services.Export.ExportAsync(session);
        var lines = await File.ReadAllLinesAsync(path);

        Assert.Contains("format: slim", lines);
        Assert.Contains("RX\tAA 55", lines);
        Assert.Contains("TX\t0D 0A", lines);
    }

    [Fact]
    public async Task ExportAsync_WritesDetailedJsonLinesWithAttributes()
    {
        var services = await CreateServicesAsync();
        services.Settings.Current.Export.DefaultSessionLogFormat = SessionLogExportFormat.DetailedJsonLines;
        var session = new Session { Id = "session-detailed", Name = "Detailed Session" };
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source.endpoint"] = "127.0.0.1:5020",
            ["transport"] = "udp"
        };
        services.Store.Append(session.Id, DateTime.UtcNow, FrameDirection.Rx, [0x48, 0x69], MessageFormat.Text, "udp", attributes);

        var path = await services.Export.ExportAsync(session);
        var jsonLine = (await File.ReadAllLinesAsync(path)).Last(line => line.StartsWith('{'));

        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(1, root.GetProperty("frameId").GetInt64());
        Assert.Equal("RX", root.GetProperty("direction").GetString());
        Assert.Equal("udp", root.GetProperty("source").GetString());
        Assert.Equal("48 69", root.GetProperty("payloadHex").GetString());
        Assert.Equal("127.0.0.1:5020", root.GetProperty("attributes").GetProperty("source.endpoint").GetString());
        Assert.Equal("udp", root.GetProperty("attributes").GetProperty("transport").GetString());
    }

    [Fact]
    public async Task ExportAsync_WritesArchiveSourceWhenRequested()
    {
        var services = await CreateServicesAsync();
        services.Settings.Current.Export.DefaultPayloadRenderMode = PayloadRenderMode.Hex;
        var session = new Session { Id = "session-archive-export", Name = "Archive Export Session", ArchiveState = SessionArchiveState.Enabled };
        services.EventBus.Publish(new SessionCreatedEvent(session));
        services.ArchiveStore.Append(new MessageFrameRecord(4, session.Id, DateTime.UtcNow, FrameDirection.Rx, [0xCA, 0xFE], MessageFormat.Hex, "archive"));

        var path = await services.Export.ExportAsync(session, source: MessageFrameDataSource.Archive);
        var text = await File.ReadAllTextAsync(path);

        Assert.Contains("source: Archive", text);
        Assert.Contains("firstFrameId: 4", text);
        Assert.Contains("lastFrameId: 4", text);
        Assert.Contains("exportedFrames: 1", text);
        Assert.Contains("CA FE", text);
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

    private async Task<TestServices> CreateServicesAsync()
    {
        var paths = new ComCrossPathService(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "config"),
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"));
        var settings = new SettingsService(new ConfigService(paths), new AppDatabase(paths), paths);
        await settings.InitializeAsync();
        settings.Current.SessionStorage.SegmentSizeLimitMb = 1;
        settings.Current.SessionStorage.PerSessionSizeLimitMb = 16;
        settings.Current.SessionStorage.GlobalSizeLimitMb = 64;
        settings.Current.Export.DefaultDirectory = paths.ExportDirectory;
        var database = new AppDatabase(paths);
        await database.InitializeAsync();
        var notification = new NotificationService(database, settings);
        var health = new StorageHealthService(notification, NullLogger<StorageHealthService>.Instance);
        var policy = new StoragePolicyService(health);
        var store = new SessionSpoolFrameStore(paths, settings, policy, health, NullLogger<SessionSpoolFrameStore>.Instance);
        var eventBus = new EventBus();
        var archiveStore = new SessionArchiveStore(paths, NullLogger<SessionArchiveStore>.Instance);
        var query = new MessageFrameQueryService(
            store,
            archiveStore,
            new SessionArchiveStateTracker(eventBus),
            NullLogger<MessageFrameQueryService>.Instance);
        var export = new ExportService(query, notification, settings, paths);
        return new TestServices(settings, store, archiveStore, eventBus, export);
    }

    private sealed record TestServices(
        SettingsService Settings,
        SessionSpoolFrameStore Store,
        SessionArchiveStore ArchiveStore,
        EventBus EventBus,
        ExportService Export);
}
