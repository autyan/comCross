using System.Text.Json;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using Xunit;

namespace ComCross.Tests.Core;

public class PluginManifestTests
{
    [Fact]
    public void Deserialize_WithPluginTypeString_BindsEnum()
    {
        const string json = """
                            {
                              "id": "serial.flow",
                              "name": "Flow Builder",
                              "version": "0.3.1",
                              "targetCoreVersion": "0.3",
                              "entryPoint": "ComCross.Plugins.Flow.FlowTool",
                              "pluginType": "FlowProcessor"
                            }
                            """;

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal(PluginType.FlowProcessor, manifest!.PluginType);
    }
}
