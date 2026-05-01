using ComCross.Core.Extensions;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
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
    public async Task SaveCurrentStateAsync_MergesSessionDescriptorsWithoutDroppingHiddenWorkloadSessions()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        await workspaceService.LoadStateAsync();

        await workspaceService.SaveStateAsync(new WorkspaceState
        {
            SessionDescriptors =
            {
                CreateDescriptor("hidden-session", parametersJson: """{"port":"COM9","baudRate":9600}""")
            }
        });

        deviceService.RestoreSession(CreateDescriptor("visible-session", parametersJson: """{"port":"COM1","baudRate":115200}"""));

        await workspaceService.SaveCurrentStateAsync(deviceService.GetAllSessions(), null);

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);

        var hidden = Assert.Single(persistedState!.SessionDescriptors, d => d.Id == "hidden-session");
        var visible = Assert.Single(persistedState.SessionDescriptors, d => d.Id == "visible-session");
        Assert.Equal("""{"port":"COM9","baudRate":9600}""", hidden.ParametersJson);
        Assert.Equal("""{"port":"COM1","baudRate":115200}""", visible.ParametersJson);
    }

    [Fact]
    public async Task SaveCurrentStateAsync_PreservesRuntimeSessionOrder()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        await workspaceService.LoadStateAsync();

        var sessionB = CreateDescriptor("session-b");
        sessionB.Name = "B";
        var sessionA = CreateDescriptor("session-a");
        sessionA.Name = "A";
        var sessionC = CreateDescriptor("session-c");
        sessionC.Name = "C";

        deviceService.RestoreSession(sessionB);
        deviceService.RestoreSession(sessionA);
        deviceService.RestoreSession(sessionC);

        await workspaceService.SaveCurrentStateAsync(deviceService.GetAllSessions(), null);

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);
        Assert.Equal(
            new[] { "session-b", "session-a", "session-c" },
            persistedState!.SessionDescriptors.Select(d => d.Id).ToArray());
    }

    [Fact]
    public async Task LoadStateAsync_RestoresSessionsInPersistedDescriptorOrder()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();

        await workspaceService.SaveStateAsync(new WorkspaceState
        {
            SessionDescriptors =
            {
                CreateDescriptor("session-third"),
                CreateDescriptor("session-first"),
                CreateDescriptor("session-second")
            }
        });

        await workspaceService.LoadStateAsync();

        Assert.Equal(
            new[] { "session-third", "session-first", "session-second" },
            deviceService.GetAllSessions().Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task SessionDescriptorPersistence_FlushAsync_PersistsImmediately()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();
        var persistence = harness.Services.GetRequiredService<SessionDescriptorPersistenceService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        await workspaceService.LoadStateAsync();
        deviceService.RestoreSession(CreateDescriptor("flush-session", parametersJson: """{"port":"COM7","baudRate":57600}"""));

        await persistence.FlushAsync();

        var persistedState = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persistedState);

        var descriptor = Assert.Single(persistedState!.SessionDescriptors, d => d.Id == "flush-session");
        Assert.Equal("""{"port":"COM7","baudRate":57600}""", descriptor.ParametersJson);
    }

    [Fact]
    public async Task SessionDisplayMetadata_RoundTripsThroughWorkspaceState()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var deviceService = harness.Services.GetRequiredService<DeviceService>();

        await workspaceService.LoadStateAsync();

        var descriptor = CreateDescriptor(
            "display-session",
            parametersJson: """{"host":"127.0.0.1","port":5020}""");
        descriptor.DisplayTitle = "TCP Client";
        descriptor.DisplaySubtitle = "127.0.0.1:58004 -> 127.0.0.1:5020";

        deviceService.RestoreSession(descriptor);
        await workspaceService.SaveCurrentStateAsync(deviceService.GetAllSessions(), deviceService.GetSession("display-session"));

        var state = await workspaceService.LoadStateAsync();
        var persisted = Assert.Single(state.SessionDescriptors, d => d.Id == "display-session");
        Assert.Equal("TCP Client", persisted.DisplayTitle);
        Assert.Equal("127.0.0.1:58004 -> 127.0.0.1:5020", persisted.DisplaySubtitle);
    }

    [Fact]
    public async Task WorkloadMembershipChanges_PublishRefreshEvents()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workspaceService = harness.Services.GetRequiredService<WorkspaceService>();
        var workloadService = harness.Services.GetRequiredService<WorkloadService>();
        var eventBus = harness.Services.GetRequiredService<IEventBus>();

        var events = new List<WorkloadSessionMembershipChangedEvent>();
        using var subscription = eventBus.Subscribe<WorkloadSessionMembershipChangedEvent>(events.Add);

        await workspaceService.LoadStateAsync();
        var activeWorkloadId = await workloadService.GetActiveWorkloadIdAsync();

        await workloadService.AddSessionToActiveWorkloadIfMissingAsync("session-membership");
        await workloadService.RemoveSessionFromAllWorkloadsAsync("session-membership");

        Assert.Contains(events, e =>
            e.WorkloadId == activeWorkloadId
            && e.SessionId == "session-membership"
            && e.IsMember);
        Assert.Contains(events, e =>
            e.WorkloadId == activeWorkloadId
            && e.SessionId == "session-membership"
            && !e.IsMember);
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

        deviceService.RestoreSession(CreateDescriptor("listener-1", managedResourceKinds: new[] { "pending" }));
        deviceService.RestoreSession(CreateDescriptor("child-1", parentSessionId: "listener-1"));
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

    private static SessionDescriptor CreateDescriptor(
        string sessionId,
        string? parentSessionId = null,
        string parametersJson = """{"port":"COM1","baudRate":115200}""",
        IReadOnlyList<string>? managedResourceKinds = null)
    {
        return new SessionDescriptor
        {
            Id = sessionId,
            Name = sessionId,
            AdapterId = "plugin:serial.adapter:serial",
            PluginId = "serial.adapter",
            CapabilityId = "serial",
            ParametersJson = parametersJson,
            ParentSessionId = parentSessionId,
            ManagedResourceKinds = managedResourceKinds?.ToList() ?? new List<string>()
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
