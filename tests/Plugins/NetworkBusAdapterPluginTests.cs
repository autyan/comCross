using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Plugins.Network;
using Xunit;

namespace ComCross.Tests.Plugins;

public sealed class NetworkBusAdapterPluginTests
{
    [Fact]
    public async Task TcpListener_PendingResource_CanBeBoundViaFormalTarget()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeTcpPort();

        var listenParams = JsonSerializer.SerializeToElement(new
        {
            listenHost = "127.0.0.1",
            listenPort,
            backlog = 8
        });

        var listenResult = await plugin.ConnectAsync(
            new PluginConnectCommand("tcp.server", listenParams, "listener-tcp"),
            CancellationToken.None);

        Assert.True(listenResult.Ok, listenResult.Error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort);

        var pendingId = await WaitForPendingAsync(plugin, "tcp.server", "listener-tcp");
        Assert.False(string.IsNullOrWhiteSpace(pendingId));

        var bindParams = JsonSerializer.SerializeToElement(new
        {
            host = "127.0.0.1",
            port = listenPort,
            endpoint = $"127.0.0.1:{listenPort}"
        });

        var bindResult = await plugin.ConnectAsync(
            new PluginConnectCommand(
                "tcp.server",
                bindParams,
                "child-tcp",
                ScopeSessionId: "listener-tcp",
                ResourceKind: "pending",
                ResourceId: pendingId),
            CancellationToken.None);

        Assert.True(bindResult.Ok, bindResult.Error);

        await plugin.DisconnectAsync(new PluginDisconnectCommand("child-tcp"), CancellationToken.None);
        await plugin.DisconnectAsync(new PluginDisconnectCommand("listener-tcp"), CancellationToken.None);
    }

    [Fact]
    public async Task UdpListener_PendingResource_CanBeBoundViaFormalTarget()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeUdpPort();

        var listenParams = JsonSerializer.SerializeToElement(new
        {
            listenHost = "127.0.0.1",
            listenPort
        });

        var listenResult = await plugin.ConnectAsync(
            new PluginConnectCommand("udp.listen", listenParams, "listener-udp"),
            CancellationToken.None);

        Assert.True(listenResult.Ok, listenResult.Error);

        using var sender = new UdpClient();
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        await sender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, listenPort));

        var pendingId = await WaitForPendingAsync(plugin, "udp.listen", "listener-udp");
        Assert.False(string.IsNullOrWhiteSpace(pendingId));

        var bindParams = JsonSerializer.SerializeToElement(new
        {
            remoteHost = "127.0.0.1",
            remotePort = sender.Client.LocalEndPoint is IPEndPoint ep ? ep.Port : 0,
            endpoint = sender.Client.LocalEndPoint?.ToString()
        });

        var bindResult = await plugin.ConnectAsync(
            new PluginConnectCommand(
                "udp.listen",
                bindParams,
                "child-udp",
                ScopeSessionId: "listener-udp",
                ResourceKind: "pending",
                ResourceId: pendingId),
            CancellationToken.None);

        Assert.True(bindResult.Ok, bindResult.Error);

        await plugin.DisconnectAsync(new PluginDisconnectCommand("child-udp"), CancellationToken.None);
        await plugin.DisconnectAsync(new PluginDisconnectCommand("listener-udp"), CancellationToken.None);
    }

    [Fact]
    public async Task TcpListener_PendingResource_CanBeRejectedViaCustomAction()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeTcpPort();

        var listenParams = JsonSerializer.SerializeToElement(new
        {
            listenHost = "127.0.0.1",
            listenPort,
            backlog = 8
        });

        var listenResult = await plugin.ConnectAsync(
            new PluginConnectCommand("tcp.server", listenParams, "listener-tcp-reject"),
            CancellationToken.None);

        Assert.True(listenResult.Ok, listenResult.Error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort);

        var pendingId = await WaitForPendingAsync(plugin, "tcp.server", "listener-tcp-reject");
        Assert.False(string.IsNullOrWhiteSpace(pendingId));

        var rejectResult = await plugin.ExecuteActionAsync(
            new PluginActionCommand(
                "network.reject-pending",
                "listener-tcp-reject",
                JsonSerializer.SerializeToElement(new { pendingId })),
            CancellationToken.None);

        Assert.True(rejectResult.Ok, rejectResult.Error);

        var snapshot = plugin.GetUiState(new PluginUiStateQuery(
            "tcp.server",
            "listener-tcp-reject",
            ViewKind: "listener",
            ViewInstanceId: null,
            ResourceKind: "pending",
            ResourceId: "all"));

        Assert.True(snapshot.State.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Empty(items.EnumerateArray());

        await plugin.DisconnectAsync(new PluginDisconnectCommand("listener-tcp-reject"), CancellationToken.None);
    }

    private static async Task<string?> WaitForPendingAsync(
        NetworkBusAdapterPlugin plugin,
        string capabilityId,
        string sessionId)
    {
        for (var i = 0; i < 50; i++)
        {
            var snapshot = plugin.GetUiState(new PluginUiStateQuery(
                capabilityId,
                sessionId,
                ViewKind: "listener",
                ViewInstanceId: null,
                ResourceKind: "pending",
                ResourceId: "all"));

            if (snapshot.State.ValueKind == JsonValueKind.Object
                && snapshot.State.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("id", out var idEl)
                        && idEl.ValueKind == JsonValueKind.String)
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            return id;
                        }
                    }
                }
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetFreeUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }
}
