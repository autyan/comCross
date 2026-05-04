using ComCross.Core.Services;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class SessionSpoolFrameStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "comcross-spool-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Append_ReadAfter_PreservesFrameFacts()
    {
        var store = CreateStore();
        var timestamp = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc);
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source.endpoint"] = "127.0.0.1:9000"
        };

        var frameId = store.Append(
            "session-a",
            timestamp,
            FrameDirection.Rx,
            payload,
            MessageFormat.Hex,
            "udp",
            attributes);

        var frames = store.ReadAfter("session-a", 0, 10, out var firstAvailable);

        Assert.Equal(1, frameId);
        Assert.Equal(1, firstAvailable);
        var frame = Assert.Single(frames);
        Assert.Equal(1, frame.FrameId);
        Assert.Equal("session-a", frame.SessionId);
        Assert.Equal(timestamp, frame.TimestampUtc);
        Assert.Equal(FrameDirection.Rx, frame.Direction);
        Assert.Equal(payload, frame.RawData);
        Assert.Equal(MessageFormat.Hex, frame.Format);
        Assert.Equal("udp", frame.Source);
        Assert.Equal("127.0.0.1:9000", frame.Attributes["source.endpoint"]);
    }

    [Fact]
    public void Cleanup_RemovesOldSealedSegmentsOnly()
    {
        var store = CreateStore();
        var payload = new byte[800 * 1024];

        store.Append("session-cleanup", DateTime.UtcNow, FrameDirection.Rx, payload, MessageFormat.Hex, "rx");
        store.Append("session-cleanup", DateTime.UtcNow, FrameDirection.Rx, payload, MessageFormat.Hex, "rx");
        store.Append("session-cleanup", DateTime.UtcNow, FrameDirection.Rx, payload, MessageFormat.Hex, "rx");

        var info = store.GetWindowInfo("session-cleanup");
        var frames = store.ReadAfter("session-cleanup", 0, 10, out var firstAvailable);

        Assert.True(info.FirstAvailableFrameId > 1);
        Assert.Equal(info.FirstAvailableFrameId, firstAvailable);
        Assert.Equal(3, info.LastFrameId);
        Assert.True(info.DroppedFrames > 0);
        Assert.All(frames, frame => Assert.True(frame.FrameId >= firstAvailable));
        Assert.Contains(frames, frame => frame.FrameId == 3);
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

    private SessionSpoolFrameStore CreateStore()
    {
        var paths = new ComCrossPathService(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "config"),
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"));
        var settings = new SettingsService(new ConfigService(paths), new AppDatabase(paths), paths);
        settings.Current.Logs.MaxFileSizeMb = 1;
        settings.Current.Logs.MaxPerSessionSizeMb = 1;
        settings.Current.Logs.MaxTotalSizeMb = 16;
        return new SessionSpoolFrameStore(paths, settings, NullLogger<SessionSpoolFrameStore>.Instance);
    }
}
