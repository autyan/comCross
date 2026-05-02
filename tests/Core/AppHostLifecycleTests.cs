using ComCross.Core.Application;
using ComCross.Core.Extensions;
using ComCross.Core.Models;
using ComCross.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class AppHostLifecycleTests
{
    [Fact]
    public async Task ShutdownAsync_IsIdempotent_AndClearsRestoredSessions()
    {
        await using var harness = await TestHarness.CreateAsync();

        var appHost = harness.Services.GetRequiredService<IAppHost>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();

        deviceService.RestoreSession(CreateDescriptor("shutdown-session"));
        Assert.NotNull(deviceService.GetSession("shutdown-session"));

        await appHost.ShutdownAsync();
        await appHost.ShutdownAsync();

        Assert.Empty(deviceService.GetAllSessions());
        Assert.Null(deviceService.GetSession("shutdown-session"));
    }

    private static SessionDescriptor CreateDescriptor(string sessionId)
    {
        return new SessionDescriptor
        {
            Id = sessionId,
            Name = sessionId,
            AdapterId = "plugin:serial.adapter:serial",
            PluginId = "serial.adapter",
            CapabilityId = "serial",
            ParametersJson = """{"port":"COM1","baudRate":115200}"""
        };
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly string _configDirectory;

        private TestHarness(IServiceProvider services, string configDirectory)
        {
            Services = services;
            _configDirectory = configDirectory;
        }

        public IServiceProvider Services { get; }

        public static Task<TestHarness> CreateAsync()
        {
            var configDirectory = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDirectory);

            var services = new ServiceCollection();
            services.AddComCrossCore();
            services.AddSingleton(new ComCrossPathService(AppContext.BaseDirectory, configDirectory));
            services.AddSingleton(new ConfigService(configDirectory));

            return Task.FromResult(new TestHarness(services.BuildServiceProvider(), configDirectory));
        }

        public async ValueTask DisposeAsync()
        {
            if (Services is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            try
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
