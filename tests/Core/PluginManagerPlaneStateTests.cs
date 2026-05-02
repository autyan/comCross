using System.Collections.Concurrent;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class PluginManagerPlaneStateTests
{
    [Fact]
    public void ReplacePlane_ReplacesOnlyTargetPlaneEntries()
    {
        var known = new ConcurrentDictionary<string, PluginRuntime>(StringComparer.Ordinal);
        var active = new ConcurrentDictionary<string, PluginRuntime>(StringComparer.Ordinal);

        var oldBus = CreateRuntime("serial.adapter", PluginType.BusAdapter, PluginLoadState.Loaded);
        var oldExtension = CreateRuntime("serial.stats", PluginType.Statistics, PluginLoadState.Loaded);
        known[oldBus.Info.Manifest.Id] = oldBus;
        known[oldExtension.Info.Manifest.Id] = oldExtension;
        active[oldBus.Info.Manifest.Id] = oldBus;
        active[oldExtension.Info.Manifest.Id] = oldExtension;

        var newBus = CreateRuntime("network.adapter", PluginType.BusAdapter, PluginLoadState.Disabled);
        PluginManagerPlaneState.ReplacePlane(known, active, PluginPlane.Bus, new[] { newBus });

        Assert.DoesNotContain("serial.adapter", known.Keys);
        Assert.Contains("serial.stats", known.Keys);
        Assert.Contains("network.adapter", known.Keys);

        Assert.DoesNotContain("serial.adapter", active.Keys);
        Assert.Contains("serial.stats", active.Keys);
        Assert.DoesNotContain("network.adapter", active.Keys);
    }

    private static PluginRuntime CreateRuntime(string pluginId, PluginType pluginType, PluginLoadState state)
    {
        var runtime = new PluginRuntime(new PluginInfo
        {
            AssemblyPath = "/tmp/" + pluginId + ".dll",
            Manifest = new PluginManifest
            {
                Id = pluginId,
                Name = pluginId,
                EntryPoint = pluginId + ".Entry",
                PluginType = pluginType
            }
        });

        switch (state)
        {
            case PluginLoadState.Loaded:
                runtime.SetLoaded(Array.Empty<PluginCapabilityDescriptor>(), null);
                break;
            case PluginLoadState.Disabled:
                runtime = PluginRuntime.Disabled(runtime.Info);
                break;
            default:
                runtime.SetFailed("failed");
                break;
        }

        return runtime;
    }
}
