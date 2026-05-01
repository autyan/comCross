using System.Text.Json;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class ExtensionActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_SendDataToSession_ForwardsToWorkspaceCoordinator()
    {
        var coordinator = new FakeWorkspaceCoordinator();
        var services = new ServiceCollection()
            .AddSingleton<IWorkspaceCoordinator>(coordinator)
            .BuildServiceProvider();

        var executor = new ExtensionActionExecutor(services, NullLogger<ExtensionActionExecutor>.Instance);
        var runtime = CreateRuntime("serial.stats", PluginType.Statistics);
        var request = new PluginHostExtensionActionRequestEvent(
            PluginId: "serial.stats",
            Action: ExtensionActionNames.SendDataToSession,
            SessionId: "session-1",
            Payload: JsonSerializer.SerializeToElement(new ExtensionSendDataRequest("session-1", new byte[] { 0x01, 0x02, 0x03 })));

        await executor.ExecuteAsync(runtime, request);

        Assert.Equal("session-1", coordinator.LastSessionId);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, coordinator.LastData);
    }

    [Fact]
    public async Task ExecuteAsync_PublishNotification_PersistsNotification()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new ConfigService(tempDir);
            var database = new AppDatabase(tempDir);
            await database.InitializeAsync();
            var settings = new SettingsService(config, database);
            var notificationService = new NotificationService(database, settings);

            var services = new ServiceCollection()
                .AddSingleton(notificationService)
                .BuildServiceProvider();

            var executor = new ExtensionActionExecutor(services, NullLogger<ExtensionActionExecutor>.Instance);
            var runtime = CreateRuntime("serial.stats", PluginType.Statistics);
            var request = new PluginHostExtensionActionRequestEvent(
                PluginId: "serial.stats",
                Action: ExtensionActionNames.PublishNotification,
                Payload: JsonSerializer.SerializeToElement(new ExtensionNotificationRequest(
                    "serial.stats.notice",
                    new object[] { "abc", 42 },
                    "System",
                    "Warning")));

            await executor.ExecuteAsync(runtime, request);

            var items = await notificationService.GetRecentAsync(limit: 10);
            var item = Assert.Single(items);
            Assert.Equal(NotificationCategory.System, item.Category);
            Assert.Equal(NotificationLevel.Warning, item.Level);
            Assert.Equal("serial.stats.notice", item.MessageKey);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static PluginRuntime CreateRuntime(string pluginId, PluginType pluginType)
    {
        var info = new PluginInfo
        {
            AssemblyPath = "/tmp/fake.dll",
            Manifest = new PluginManifest
            {
                Id = pluginId,
                Name = pluginId,
                EntryPoint = pluginId + ".Entry",
                PluginType = pluginType
            }
        };

        var runtime = new PluginRuntime(info);
        runtime.SetLoaded(Array.Empty<PluginCapabilityDescriptor>(), null);
        return runtime;
    }

    private sealed class FakeWorkspaceCoordinator : IWorkspaceCoordinator
    {
        public string? LastSessionId { get; private set; }
        public byte[]? LastData { get; private set; }

        public long TotalRxBytes => 0;
        public long TotalTxBytes => 0;
        public event EventHandler? StatisticsUpdated
        {
            add { }
            remove { }
        }

        public Task<IEnumerable<Session>> GetActiveSessionsAsync() => Task.FromResult<IEnumerable<Session>>(Array.Empty<Session>());
        public Task<ComCross.Core.Models.Workload?> GetCurrentWorkloadAsync()
            => Task.FromResult<ComCross.Core.Models.Workload?>(null);
        public Task SwitchWorkloadAsync(string workloadId) => Task.CompletedTask;
        public Task CloseSessionAsync(string sessionId) => Task.CompletedTask;
        public Task<WorkspaceState> LoadStateAsync() => Task.FromResult(new WorkspaceState());
        public Task SaveCurrentStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll) => Task.CompletedTask;
        public Task EnsureDefaultWorkloadAsync() => Task.CompletedTask;
        public Task<Session> ConnectAsync(
            string pluginId,
            string capabilityId,
            string parametersJson,
            string? sessionName = null,
            string? scopeSessionId = null,
            string? resourceKind = null,
            string? resourceId = null) => throw new NotSupportedException();
        public Task RenameSessionAsync(string sessionId, string name) => Task.CompletedTask;
        public Task DeleteSessionAsync(string sessionId) => Task.CompletedTask;
        public Task<PluginCommandResult> SendMessageAsync(
            string sessionId,
            string message,
            MessageFormat format,
            bool addCr,
            bool addLf,
            string? transmitTargetId = null)
            => Task.FromResult(new PluginCommandResult(true));

        public Task<PluginCommandResult> SendDataAsync(string sessionId, byte[] data, string? transmitTargetId = null)
        {
            LastSessionId = sessionId;
            LastData = data;
            return Task.FromResult(new PluginCommandResult(true));
        }

        public Task<PluginTransmitTargetSnapshot> GetTransmitTargetsAsync(string sessionId)
            => Task.FromResult(new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>()));

        public void ClearMessages(string sessionId)
        {
        }

        public void SubscribeToMessages(string sessionId, Action<LogMessage> callback)
        {
        }

        public Task<string> ExportAsync(Session session, string? searchQuery = null, string? customFilePath = null)
            => Task.FromResult(string.Empty);
    }
}
