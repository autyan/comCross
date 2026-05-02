using ComCross.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class BundledPluginSynchronizationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ComCross.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SynchronizeCopiesBundledPluginPackagesIntoRuntimeRoot()
    {
        var installDirectory = Path.Combine(_root, "install");
        var localDataDirectory = Path.Combine(_root, "local-data");
        var sourcePlugin = Path.Combine(installDirectory, "bundled-plugins", "serial.adapter-3mpdru6x");
        Directory.CreateDirectory(sourcePlugin);
        File.WriteAllText(Path.Combine(sourcePlugin, "ComCross.Plugins.Serial.dll"), "new");

        var runtimePlugin = Path.Combine(localDataDirectory, "plugins", "serial.adapter-3mpdru6x");
        Directory.CreateDirectory(runtimePlugin);
        File.WriteAllText(Path.Combine(runtimePlugin, "stale.dll"), "old");

        var service = new BundledPluginSynchronizationService(
            new ComCrossPathService(installDirectory, localDataDirectory),
            NullLogger<BundledPluginSynchronizationService>.Instance);

        service.Synchronize();

        Assert.True(File.Exists(Path.Combine(runtimePlugin, "ComCross.Plugins.Serial.dll")));
        Assert.False(File.Exists(Path.Combine(runtimePlugin, "stale.dll")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
