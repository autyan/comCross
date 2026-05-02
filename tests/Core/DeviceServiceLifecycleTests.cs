using ComCross.Core.Extensions;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class DeviceServiceLifecycleTests
{
    [Fact]
    public async Task DisposeAsync_ClearsSessionsAndFrameStore()
    {
        await using var harness = await TestHarness.CreateAsync();

        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var frameStore = harness.Services.GetRequiredService<IFrameStore>();
        const string sessionId = "dispose-session";

        deviceService.RestoreSession(CreateDescriptor(sessionId));
        frameStore.Append(sessionId, DateTime.UtcNow, FrameDirection.Rx, new byte[] { 0x01, 0x02 }, MessageFormat.Hex, "test");

        Assert.NotNull(deviceService.GetSession(sessionId));
        Assert.NotEmpty(frameStore.ReadAfter(sessionId, 0, 10, out _));

        await deviceService.DisposeAsync();
        await deviceService.DisposeAsync();

        Assert.Null(deviceService.GetSession(sessionId));
        Assert.Empty(deviceService.GetAllSessions());
        Assert.Empty(frameStore.ReadAfter(sessionId, 0, 10, out _));

        var window = frameStore.GetWindowInfo(sessionId);
        Assert.Equal(0, window.LastFrameId);
        Assert.Equal(0, window.DroppedFrames);
    }

    [Fact]
    public async Task PluginHostSessionClosedEvent_MarksSessionDisconnected()
    {
        await using var harness = await TestHarness.CreateAsync();

        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var eventBus = harness.Services.GetRequiredService<IEventBus>();
        const string sessionId = "remote-closed-session";

        deviceService.RestoreSession(CreateDescriptor(sessionId));
        var session = deviceService.GetSession(sessionId);
        Assert.NotNull(session);
        session!.Status = SessionStatus.Connected;

        SessionClosedEvent? closed = null;
        using var subscription = eventBus.Subscribe<SessionClosedEvent>(evt =>
        {
            if (string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal))
            {
                closed = evt;
            }
        });

        eventBus.Publish(new PluginHostSessionClosedCoreEvent(
            "serial.adapter",
            sessionId,
            Reason: "remote-eof",
            RemoteInitiated: true));

        for (var i = 0; i < 50 && closed is null; i++)
        {
            await Task.Delay(20);
        }

        Assert.NotNull(closed);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
        Assert.Equal("remote-eof", closed!.Reason);
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
