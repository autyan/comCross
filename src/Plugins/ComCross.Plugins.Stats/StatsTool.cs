using System;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Plugins.Stats;

public sealed class StatsTool : IExtensionPlugin, IPluginNotificationSubscriber, IExtensionContextConsumer, IExtensionFrameBatchConsumer
{
    private string _title = "Stats Tool";
    private string? _lastActiveWorkloadId;
    private int _knownSessionCount;
    private long _totalFrames;
    private long _totalBytes;

    public PluginMetadata Metadata { get; } = new()
    {
        Id = "serial.stats",
        Name = "Stats Panel",
        Version = "0.3.1",
        Type = PluginType.Statistics
    };

    public string Title => _title;
    public string? LastActiveWorkloadId => _lastActiveWorkloadId;
    public int KnownSessionCount => _knownSessionCount;
    public long TotalFrames => _totalFrames;
    public long TotalBytes => _totalBytes;

    public void OnNotification(PluginNotification notification)
    {
        if (notification.Type != PluginNotificationTypes.LanguageChanged)
        {
            return;
        }

        var cultureCode = notification.GetData("culture") ?? string.Empty;
        _title = cultureCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "Stats Tool (ZH)"
            : "Stats Tool";
    }

    public void OnContextSnapshot(ExtensionContextSnapshot snapshot)
    {
        _lastActiveWorkloadId = snapshot.ActiveWorkloadId;
        _knownSessionCount = snapshot.Sessions.Count;
    }

    public void OnFrameBatch(IReadOnlyList<ExtensionFrame> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        _totalFrames += frames.Count;
        foreach (var frame in frames)
        {
            _totalBytes += frame.RawData?.Length ?? 0;
        }
    }
}
