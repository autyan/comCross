using ComCross.Core.Services;
using ComCross.PluginSdk;
using Xunit;

namespace ComCross.Tests.Core;

public class PluginPlaneClassifierTests
{
    [Fact]
    public void TryClassify_BusAdapter_GoesToBusPlane()
    {
        var manifest = new PluginManifest
        {
            Id = "serial.adapter",
            Name = "Serial Adapter",
            Version = "0.3.2",
            EntryPoint = "ComCross.Plugins.Serial.SerialBusAdapterPlugin",
            PluginType = PluginType.BusAdapter
        };

        var ok = PluginPlaneClassifier.TryClassify(manifest, out var plane, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PluginPlane.Bus, plane);
    }

    [Theory]
    [InlineData(PluginType.FlowProcessor)]
    [InlineData(PluginType.Statistics)]
    [InlineData(PluginType.UIExtension)]
    [InlineData(PluginType.Extension)]
    public void TryClassify_NonBusTypes_GoToExtensionPlane(PluginType pluginType)
    {
        var manifest = new PluginManifest
        {
            Id = "extension.plugin",
            Name = "Extension Plugin",
            Version = "0.3.2",
            EntryPoint = "ComCross.Plugins.Extension.Plugin",
            PluginType = pluginType
        };

        var ok = PluginPlaneClassifier.TryClassify(manifest, out var plane, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PluginPlane.Extension, plane);
    }

    [Fact]
    public void TryClassify_MissingPluginType_Fails()
    {
        var manifest = new PluginManifest
        {
            Id = "legacy.plugin",
            Name = "Legacy Plugin",
            Version = "0.3.2",
            EntryPoint = "Legacy.Plugin"
        };

        var ok = PluginPlaneClassifier.TryClassify(manifest, out _, out var error);

        Assert.False(ok);
        Assert.Contains("pluginType", error);
    }
}
