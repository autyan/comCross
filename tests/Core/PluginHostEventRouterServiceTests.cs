using System.Text.Json;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class PluginHostEventRouterServiceTests
{
    [Fact]
    public async Task RouteAsync_RefreshesUiStateAndPublishesCoreEvent()
    {
        var eventBus = new EventBus();
        var stateManager = new PluginUiStateManager();
        var fetcher = new FakePluginUiStateFetcher(
            new PluginUiStateSnapshot(
                JsonDocument.Parse("""{"ports":["COM3","COM5"],"connected":false}""").RootElement.Clone(),
                DateTimeOffset.UtcNow));
        var service = new PluginHostEventRouterService(
            eventBus,
            stateManager,
            fetcher,
            new FakeExtensionActionExecutor(),
            NullLogger<PluginHostEventRouterService>.Instance);

        PluginUiStateInvalidatedCoreEvent? published = null;
        using var subscription = eventBus.Subscribe<PluginUiStateInvalidatedCoreEvent>(evt => published = evt);

        var runtime = CreateRuntime("serial.adapter", PluginType.BusAdapter);
        var payload = JsonSerializer.SerializeToElement(new PluginHostUiStateInvalidatedEvent(
            CapabilityId: "serial",
            SessionId: null,
            ViewKind: "connect-dialog",
            ViewInstanceId: "dlg-1",
            Reason: "ports-changed"));

        await service.RouteAsync(runtime, new PluginHostEvent(PluginHostEventTypes.UiStateInvalidated, payload));

        Assert.NotNull(published);
        Assert.Equal("serial.adapter", published!.PluginId);
        Assert.Equal("serial", published.CapabilityId);
        Assert.Equal("connect-dialog", published.ViewKind);

        var state = stateManager.GetState(PluginUiViewScope.From("connect-dialog", "dlg-1"), null);
        Assert.True(state.ContainsKey("ports"));
        Assert.True(state.ContainsKey("connected"));
        Assert.Equal(1, fetcher.CallCount);
    }

    [Fact]
    public async Task RouteAsync_IgnoresInvalidPayload()
    {
        var eventBus = new EventBus();
        var stateManager = new PluginUiStateManager();
        var fetcher = new FakePluginUiStateFetcher(null);
        var service = new PluginHostEventRouterService(
            eventBus,
            stateManager,
            fetcher,
            new FakeExtensionActionExecutor(),
            NullLogger<PluginHostEventRouterService>.Instance);

        var runtime = CreateRuntime("serial.adapter", PluginType.BusAdapter);
        var payload = JsonSerializer.SerializeToElement(new { capabilityId = "" });

        await service.RouteAsync(runtime, new PluginHostEvent(PluginHostEventTypes.UiStateInvalidated, payload));

        Assert.Equal(0, fetcher.CallCount);
        Assert.Empty(stateManager.GetState(PluginUiViewScope.From("connect-dialog"), null));
    }

    [Fact]
    public async Task RouteAsync_ForExtensionActionRequest_DelegatesToExecutor()
    {
        var eventBus = new EventBus();
        var stateManager = new PluginUiStateManager();
        var fetcher = new FakePluginUiStateFetcher(null);
        var executor = new FakeExtensionActionExecutor();
        var service = new PluginHostEventRouterService(
            eventBus,
            stateManager,
            fetcher,
            executor,
            NullLogger<PluginHostEventRouterService>.Instance);

        var runtime = CreateRuntime("serial.stats", PluginType.Statistics);
        var payload = JsonSerializer.SerializeToElement(new PluginHostExtensionActionRequestEvent(
            PluginId: "serial.stats",
            Action: ExtensionActionNames.PublishNotification,
            Payload: JsonSerializer.SerializeToElement(new ExtensionNotificationRequest("serial.stats.notice"))));

        await service.RouteAsync(runtime, new PluginHostEvent(PluginHostEventTypes.ExtensionActionRequested, payload));

        Assert.NotNull(executor.LastRequest);
        Assert.Equal("serial.stats", executor.LastRequest!.PluginId);
        Assert.Equal(ExtensionActionNames.PublishNotification, executor.LastRequest.Action);
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

    private sealed class FakePluginUiStateFetcher : IPluginUiStateFetcher
    {
        private readonly PluginUiStateSnapshot? _snapshot;

        public FakePluginUiStateFetcher(PluginUiStateSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public int CallCount { get; private set; }

        public Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
            PluginRuntime runtime,
            PluginHostUiStateInvalidatedEvent invalidated,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_snapshot is null
                ? (false, "no snapshot", (PluginUiStateSnapshot?)null)
                : (true, (string?)null, _snapshot));
        }
    }

    private sealed class FakeExtensionActionExecutor : IExtensionActionExecutor
    {
        public PluginHostExtensionActionRequestEvent? LastRequest { get; private set; }

        public Task ExecuteAsync(
            PluginRuntime runtime,
            PluginHostExtensionActionRequestEvent request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }
    }
}
