using ComCross.PluginSdk;
using ComCross.Plugins.Stats;
using Xunit;

namespace ComCross.Tests.Plugins;

public class StatsToolExtensionDataTests
{
    [Fact]
    public void OnContextSnapshot_TracksActiveWorkloadAndSessionCount()
    {
        var tool = new StatsTool();
        var snapshot = new ExtensionContextSnapshot(
            Sessions:
            [
                new ExtensionSessionSnapshot("s1", "Session 1", "plugin:test:a", "plugin.test", "a", "Connected", "Connection", null, null, 0, 0),
                new ExtensionSessionSnapshot("s2", "Session 2", "plugin:test:b", "plugin.test", "b", "Disconnected", "Connection", null, null, 0, 0)
            ],
            Workloads: Array.Empty<ExtensionWorkloadSnapshot>(),
            ActiveWorkloadId: "w1",
            Language: "en-US",
            Settings: default);

        tool.OnContextSnapshot(snapshot);

        Assert.Equal("w1", tool.LastActiveWorkloadId);
        Assert.Equal(2, tool.KnownSessionCount);
    }

    [Fact]
    public void OnFrameBatch_AggregatesDerivedStatistics()
    {
        var tool = new StatsTool();

        tool.OnFrameBatch(
        [
            new ExtensionFrame(1, "s1", DateTime.UtcNow, "Rx", [0x01, 0x02], "Hex", "shm-rx"),
            new ExtensionFrame(2, "s1", DateTime.UtcNow, "Tx", [0x03], "Text", "send-tx")
        ]);

        Assert.Equal(2, tool.TotalFrames);
        Assert.Equal(3, tool.TotalBytes);
    }
}
