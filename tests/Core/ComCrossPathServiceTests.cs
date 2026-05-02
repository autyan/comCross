using ComCross.Core.Application;
using ComCross.Core.Services;
using ComCross.Platform.UserDirectories;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class ComCrossPathServiceTests
{
    [Fact]
    public void ComposesDirectoriesFromPlatformHomesAndInstanceDirectoryName()
    {
        var provider = new TestUserDirectoryProvider("/config", "/data", "/cache");
        var instance = new ComCrossInstanceIdentity(
            1,
            "ComCross",
            "Dev",
            "ComCrossDev",
            "comcross-dev",
            "v0",
            ManifestPath: null);

        var paths = new ComCrossPathService(provider, instance, "/install");

        Assert.Equal("/install", paths.InstallDirectory);
        Assert.Equal(Path.Combine("/config", "ComCrossDev"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine("/data", "ComCrossDev"), paths.LocalDataDirectory);
        Assert.Equal(Path.Combine("/cache", "ComCrossDev"), paths.CacheDirectory);
        Assert.Equal(Path.Combine("/data", "ComCrossDev", "logs", "startup"), paths.StartupLogDirectory);
        Assert.Equal("comcross-dev", paths.Instance.InstanceId);
    }

    private sealed record TestUserDirectoryProvider(
        string ConfigHome,
        string LocalDataHome,
        string CacheHome) : IPlatformUserDirectoryProvider;
}
