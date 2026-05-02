using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Models;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class CommandExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_TextCommand_UsesWorkspaceMessagePipeline()
    {
        var coordinator = new FakeWorkspaceCoordinator();
        var service = new CommandExecutionService(coordinator);
        var command = new CommandDefinition
        {
            Payload = "AT",
            Type = CommandPayloadType.Text,
            AppendCr = true
        };

        await service.ExecuteAsync("session-1", command);

        Assert.Equal("session-1", coordinator.LastMessageSessionId);
        Assert.Equal("AT", coordinator.LastMessage);
        Assert.Equal(MessageFormat.Text, coordinator.LastFormat);
        Assert.True(coordinator.LastAddCr);
        Assert.False(coordinator.LastAddLf);
        Assert.Null(coordinator.LastData);
    }

    [Fact]
    public async Task ExecuteAsync_HexCommand_UsesWorkspaceMessagePipeline()
    {
        var coordinator = new FakeWorkspaceCoordinator();
        var service = new CommandExecutionService(coordinator);
        var command = new CommandDefinition
        {
            Payload = "01 02 0A",
            Type = CommandPayloadType.Hex,
            AppendLf = true
        };

        await service.ExecuteAsync("session-1", command);

        Assert.Equal("session-1", coordinator.LastMessageSessionId);
        Assert.Equal("01 02 0A", coordinator.LastMessage);
        Assert.Equal(MessageFormat.Hex, coordinator.LastFormat);
        Assert.False(coordinator.LastAddCr);
        Assert.True(coordinator.LastAddLf);
        Assert.Null(coordinator.LastData);
    }

    [Fact]
    public async Task ExecuteAsync_NonUtf8TextCommand_PreservesConfiguredEncoding()
    {
        var coordinator = new FakeWorkspaceCoordinator();
        var service = new CommandExecutionService(coordinator);
        var command = new CommandDefinition
        {
            Payload = "A",
            Type = CommandPayloadType.Text,
            Encoding = "unicode",
            AppendLf = true
        };

        await service.ExecuteAsync("session-1", command);

        Assert.Equal("session-1", coordinator.LastDataSessionId);
        Assert.Equal(new byte[] { 0x41, 0x00, 0x0A, 0x00 }, coordinator.LastData);
        Assert.Null(coordinator.LastMessage);
    }

    private sealed class FakeWorkspaceCoordinator : IWorkspaceCoordinator
    {
        public string? LastMessageSessionId { get; private set; }
        public string? LastMessage { get; private set; }
        public MessageFormat? LastFormat { get; private set; }
        public bool LastAddCr { get; private set; }
        public bool LastAddLf { get; private set; }
        public string? LastDataSessionId { get; private set; }
        public byte[]? LastData { get; private set; }

        public long TotalRxBytes => 0;

        public long TotalTxBytes => 0;

        public event EventHandler? StatisticsUpdated
        {
            add { }
            remove { }
        }

        public Task<IEnumerable<Session>> GetActiveSessionsAsync() => Task.FromResult(Enumerable.Empty<Session>());

        public Task<Workload?> GetCurrentWorkloadAsync() => Task.FromResult<Workload?>(null);

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
            string? resourceId = null)
            => Task.FromResult(new Session { Id = "session-1", Name = "session-1" });

        public Task RenameSessionAsync(string sessionId, string name) => Task.CompletedTask;

        public Task DeleteSessionAsync(string sessionId) => Task.CompletedTask;

        public Task<PluginCommandResult> SendMessageAsync(
            string sessionId,
            string message,
            MessageFormat format,
            bool addCr,
            bool addLf,
            string? transmitTargetId = null)
        {
            LastMessageSessionId = sessionId;
            LastMessage = message;
            LastFormat = format;
            LastAddCr = addCr;
            LastAddLf = addLf;
            return Task.FromResult(new PluginCommandResult(true));
        }

        public Task<PluginCommandResult> SendDataAsync(string sessionId, byte[] data, string? transmitTargetId = null)
        {
            LastDataSessionId = sessionId;
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
