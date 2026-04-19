using ComCross.Core.Extensions;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class WorkspaceStateFlowTests
{
    [Fact]
    public async Task DeleteSessionAsync_RemovesSessionFromDeviceWorkloadsAndPersistedState()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var workloadService = harness.Services.GetRequiredService<WorkloadService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        await workspaceService.LoadStateAsync();
        var activeWorkloadId = await workloadService.GetActiveWorkloadIdAsync();

        deviceService.RestoreSession(CreateDescriptor("session-delete"));
        await workloadService.AddSessionToWorkloadAsync(activeWorkloadId, "session-delete");
        await workspaceService.SaveCurrentStateAsync(deviceService.GetAllSessions(), null);

        await workspaceService.DeleteSessionAsync("session-delete");

        Assert.Null(deviceService.GetSession("session-delete"));

        var activeWorkload = await workloadService.GetWorkloadAsync(activeWorkloadId);
        Assert.NotNull(activeWorkload);
        Assert.DoesNotContain("session-delete", activeWorkload!.SessionIds);

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);
        Assert.DoesNotContain(persistedState!.SessionDescriptors, descriptor => descriptor.Id == "session-delete");
        Assert.DoesNotContain(persistedState.Workloads.SelectMany(workload => workload.SessionIds), id => id == "session-delete");
    }

    [Fact]
    public async Task DeletedSession_DoesNotRevive_WhenDescriptorPersistenceSavesAnotherSession()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var workloadService = harness.Services.GetRequiredService<WorkloadService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        _ = harness.Services.GetRequiredService<SessionDescriptorPersistenceService>();

        await workspaceService.LoadStateAsync();
        var activeWorkloadId = await workloadService.GetActiveWorkloadIdAsync();

        deviceService.RestoreSession(CreateDescriptor("session-old"));
        await workloadService.AddSessionToWorkloadAsync(activeWorkloadId, "session-old");
        await workspaceService.SaveCurrentStateAsync(deviceService.GetAllSessions(), null);

        await workspaceService.DeleteSessionAsync("session-old");

        deviceService.RestoreSession(CreateDescriptor("session-new"));
        await Task.Delay(500);

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);
        Assert.DoesNotContain(persistedState!.SessionDescriptors, descriptor => descriptor.Id == "session-old");
        Assert.Contains(persistedState.SessionDescriptors, descriptor => descriptor.Id == "session-new");
    }

    [Fact]
    public async Task SaveCurrentStateAsync_PreservesActiveWorkloadId()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var workloadService = harness.Services.GetRequiredService<WorkloadService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        await workspaceService.LoadStateAsync();
        var createdWorkload = await workloadService.CreateWorkloadAsync("Phase D Active");
        Assert.True(await workloadService.SetActiveWorkloadAsync(createdWorkload.Id));

        await workspaceService.SaveCurrentStateAsync(Array.Empty<ComCross.Shared.Models.Session>(), null, autoScroll: false);

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);
        Assert.Equal(createdWorkload.Id, persistedState!.ActiveWorkloadId);
    }

    [Fact]
    public async Task DeleteListenerSessionAsync_RemovesChildSessionsFromRuntimeAndPersistedState()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var workloadService = harness.Services.GetRequiredService<WorkloadService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        await workspaceService.LoadStateAsync();
        var activeWorkloadId = await workloadService.GetActiveWorkloadIdAsync();

        deviceService.RestoreSession(CreateDescriptor("listener-1", SessionKind.Listener));
        deviceService.RestoreSession(CreateDescriptor("child-1", SessionKind.Connection, parentSessionId: "listener-1"));
        await workloadService.AddSessionToWorkloadAsync(activeWorkloadId, "listener-1");
        await workloadService.AddSessionToWorkloadAsync(activeWorkloadId, "child-1");
        await workspaceService.SaveCurrentStateAsync(deviceService.GetAllSessions(), null);

        await workspaceService.DeleteSessionAsync("listener-1");

        Assert.Null(deviceService.GetSession("listener-1"));
        Assert.Null(deviceService.GetSession("child-1"));

        var workload = await workloadService.GetWorkloadAsync(activeWorkloadId);
        Assert.NotNull(workload);
        Assert.DoesNotContain("listener-1", workload!.SessionIds);
        Assert.DoesNotContain("child-1", workload.SessionIds);

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);
        Assert.DoesNotContain(persistedState!.SessionDescriptors, descriptor => descriptor.Id == "listener-1");
        Assert.DoesNotContain(persistedState.SessionDescriptors, descriptor => descriptor.Id == "child-1");
    }

    private static SessionDescriptor CreateDescriptor(string sessionId, SessionKind kind = SessionKind.Connection, string? parentSessionId = null)
    {
        return new SessionDescriptor
        {
            Id = sessionId,
            Name = sessionId,
            AdapterId = "plugin:serial.adapter:serial",
            PluginId = "serial.adapter",
            CapabilityId = "serial",
            ParametersJson = """{"port":"COM1","baudRate":115200}""",
            Kind = kind,
            ParentSessionId = parentSessionId
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
