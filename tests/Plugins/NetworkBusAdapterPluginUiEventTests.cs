using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ComCross.PluginSdk;
using ComCross.Plugins.Network;
using Xunit;

namespace ComCross.Tests.Plugins;

public sealed class NetworkBusAdapterPluginUiEventTests
{
    [Fact]
    public async Task TcpListener_PendingResource_RaisesFormalInvalidationEvent()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeTcpPort();

        var invalidated = new TaskCompletionSource<PluginUiStateInvalidatedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        plugin.UiStateInvalidated += (_, evt) =>
        {
            if (evt.SessionId == "listener-event"
                && evt.ViewKind == "listener"
                && evt.ResourceKind == "pending"
                && evt.ResourceId == "all")
            {
                invalidated.TrySetResult(evt);
            }
        };

        var listenParams = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            listenHost = "127.0.0.1",
            listenPort,
            backlog = 8
        });

        var listenResult = await plugin.ConnectAsync(
            new PluginConnectCommand("tcp.server", listenParams, "listener-event"),
            CancellationToken.None);

        Assert.True(listenResult.Ok, listenResult.Error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort);

        var completed = await Task.WhenAny(invalidated.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(invalidated.Task, completed);

        var evt = await invalidated.Task;
        Assert.Equal("tcp.server", evt.CapabilityId);
        Assert.Equal("listener-event", evt.SessionId);
        Assert.Equal("pending", evt.ResourceKind);
        Assert.Equal("all", evt.ResourceId);

        await plugin.DisconnectAsync(new PluginDisconnectCommand("listener-event"), CancellationToken.None);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
