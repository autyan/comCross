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
        Assert.Equal("ServerIcon", listenResult.SessionIcon);
        Assert.Contains(PluginResourceKinds.Pending, listenResult.ManagedResourceKinds ?? Array.Empty<string>());

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
        Assert.Equal("listener-tcp", bindResult.ParentSessionId);
        Assert.Equal("NetworkIcon", bindResult.SessionIcon);

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
        Assert.Equal("ServerIcon", listenResult.SessionIcon);
        Assert.Contains(PluginResourceKinds.Pending, listenResult.ManagedResourceKinds ?? Array.Empty<string>());

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
        Assert.Equal("listener-udp", bindResult.ParentSessionId);
        Assert.Equal("NetworkIcon", bindResult.SessionIcon);

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

    [Fact]
    public async Task TcpListener_PendingResourceSnapshot_ExposesGenericResourceActions()
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
            new PluginConnectCommand("tcp.server", listenParams, "listener-tcp-contract"),
            CancellationToken.None);

        Assert.True(listenResult.Ok, listenResult.Error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort);

        var pendingId = await WaitForPendingAsync(plugin, "tcp.server", "listener-tcp-contract");
        Assert.False(string.IsNullOrWhiteSpace(pendingId));

        var snapshot = plugin.GetUiState(new PluginUiStateQuery(
            "tcp.server",
            "listener-tcp-contract",
            ViewKind: "listener",
            ViewInstanceId: null,
            ResourceKind: PluginResourceKinds.Pending,
            ResourceId: PluginResourceIds.All));

        Assert.Equal(PluginResourceKinds.Pending, snapshot.State.GetProperty("resourceKind").GetString());

        var item = Assert.Single(snapshot.State.GetProperty("items").EnumerateArray());
        Assert.Equal(pendingId, item.GetProperty("id").GetString());

        var actions = item.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Contains(actions, action =>
            action.GetProperty("id").GetString() == PluginResourceActionIds.Accept
            && action.GetProperty("kind").GetString() == PluginResourceActionKinds.ConnectScopedResource);
        Assert.Contains(actions, action =>
            action.GetProperty("id").GetString() == PluginResourceActionIds.Reject
            && action.GetProperty("kind").GetString() == PluginResourceActionKinds.ExecuteAction
            && action.GetProperty("actionName").GetString() == "network.reject-pending");

        var bulkAction = Assert.Single(snapshot.State.GetProperty("bulkActions").EnumerateArray());
        Assert.Equal(PluginResourceActionIds.RejectAll, bulkAction.GetProperty("id").GetString());
        Assert.Equal(PluginResourceActionKinds.ExecuteAction, bulkAction.GetProperty("kind").GetString());
        Assert.Equal("network.reject-all-pending", bulkAction.GetProperty("actionName").GetString());

        await plugin.DisconnectAsync(new PluginDisconnectCommand("listener-tcp-contract"), CancellationToken.None);
    }

    [Fact]
    public async Task TcpClient_RemoteClose_RaisesSessionClosedEvent()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeTcpPort();
        var listener = new TcpListener(IPAddress.Loopback, listenPort);
        listener.Start();

        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            var closedTask = WaitForSessionClosedAsync(plugin, "tcp-client-remote-close");

            var localPort = GetFreeTcpPort();
            var connectParams = JsonSerializer.SerializeToElement(new
            {
                remoteHost = "127.0.0.1",
                remotePort = listenPort,
                localHost = "127.0.0.1",
                localPort,
                connectTimeoutMs = 3000
            });

            var connectResult = await plugin.ConnectAsync(
                new PluginConnectCommand("tcp", connectParams, "tcp-client-remote-close"),
                CancellationToken.None);

            Assert.True(connectResult.Ok, connectResult.Error);

            using var accepted = await acceptTask;
            accepted.Close();

            var closed = await closedTask;
            Assert.Equal("tcp-client-remote-close", closed.SessionId);
            Assert.Equal("remote-eof", closed.Reason);
            Assert.True(closed.RemoteInitiated);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpClient_ConnectResult_IncludesCommittedEndpointMetadata()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeTcpPort();
        var listener = new TcpListener(IPAddress.Loopback, listenPort);
        listener.Start();

        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            var localPort = GetFreeTcpPort();
            var connectParams = JsonSerializer.SerializeToElement(new
            {
                remoteHost = "127.0.0.1",
                remotePort = listenPort,
                localHost = "127.0.0.1",
                localPort,
                connectTimeoutMs = 3000
            });

            var result = await plugin.ConnectAsync(
                new PluginConnectCommand("tcp", connectParams, "tcp-client-metadata"),
                CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            Assert.Equal("TCP Client", result.DisplayTitle);
            Assert.Equal("NetworkIcon", result.SessionIcon);
            Assert.NotNull(result.CommittedParameters);

            var committed = result.CommittedParameters!.Value;
            Assert.Equal("127.0.0.1", committed.GetProperty("remoteHost").GetString());
            Assert.Equal(listenPort, committed.GetProperty("remotePort").GetInt32());
            Assert.Equal("127.0.0.1", committed.GetProperty("localHost").GetString());
            Assert.Equal(localPort, committed.GetProperty("localPort").GetInt32());
            Assert.Contains(" -> ", result.DisplaySubtitle);

            using var accepted = await acceptTask;
            await plugin.DisconnectAsync(new PluginDisconnectCommand("tcp-client-metadata"), CancellationToken.None);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpClient_ConnectResult_CommitsEphemeralLocalPortForReconnect()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var listenPort = GetFreeTcpPort();
        var listener = new TcpListener(IPAddress.Loopback, listenPort);
        listener.Start();

        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            var connectParams = JsonSerializer.SerializeToElement(new
            {
                remoteHost = "127.0.0.1",
                remotePort = listenPort,
                connectTimeoutMs = 3000
            });

            var result = await plugin.ConnectAsync(
                new PluginConnectCommand("tcp", connectParams, "tcp-client-ephemeral-local"),
                CancellationToken.None);

            Assert.True(result.Ok, result.Error);
            Assert.NotNull(result.CommittedParameters);

            var committed = result.CommittedParameters!.Value;
            var actualLocalPort = committed.GetProperty("actualLocalPort").GetInt32();
            Assert.True(actualLocalPort > 0);
            Assert.Equal(actualLocalPort, committed.GetProperty("localPort").GetInt32());
            Assert.Equal(actualLocalPort, committed.GetProperty("requestedLocalPort").GetInt32());

            using var accepted = await acceptTask;
            await plugin.DisconnectAsync(new PluginDisconnectCommand("tcp-client-ephemeral-local"), CancellationToken.None);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task UdpClient_ConnectResult_IncludesLocalAndRemoteEndpointMetadata()
    {
        var plugin = new NetworkBusAdapterPlugin();
        var remotePort = GetFreeUdpPort();
        var localPort = GetFreeUdpPort();

        var connectParams = JsonSerializer.SerializeToElement(new
        {
            remoteHost = "127.0.0.1",
            remotePort,
            localHost = "127.0.0.1",
            localPort
        });

        var result = await plugin.ConnectAsync(
            new PluginConnectCommand("udp", connectParams, "udp-client-metadata"),
            CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("UDP Socket", result.DisplayTitle);
        Assert.Equal("NetworkIcon", result.SessionIcon);
        Assert.NotNull(result.CommittedParameters);

        var committed = result.CommittedParameters!.Value;
        Assert.Equal("127.0.0.1", committed.GetProperty("remoteHost").GetString());
        Assert.Equal(remotePort, committed.GetProperty("remotePort").GetInt32());
        Assert.Equal("127.0.0.1", committed.GetProperty("localHost").GetString());
        Assert.Equal(localPort, committed.GetProperty("localPort").GetInt32());
        Assert.Contains(" -> ", result.DisplaySubtitle);

        await plugin.DisconnectAsync(new PluginDisconnectCommand("udp-client-metadata"), CancellationToken.None);
    }

    [Fact]
    public async Task TcpListener_Disconnect_ClosesBoundChildSession()
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
            new PluginConnectCommand("tcp.server", listenParams, "listener-cascade"),
            CancellationToken.None);

        Assert.True(listenResult.Ok, listenResult.Error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort);

        var pendingId = await WaitForPendingAsync(plugin, "tcp.server", "listener-cascade");
        Assert.False(string.IsNullOrWhiteSpace(pendingId));

        var childClosedTask = WaitForSessionClosedAsync(plugin, "child-cascade");
        var listenerClosedTask = WaitForSessionClosedAsync(plugin, "listener-cascade");

        var bindResult = await plugin.ConnectAsync(
            new PluginConnectCommand(
                "tcp.server",
                JsonSerializer.SerializeToElement(new { endpoint = client.Client.LocalEndPoint?.ToString() }),
                "child-cascade",
                ScopeSessionId: "listener-cascade",
                ResourceKind: "pending",
                ResourceId: pendingId),
            CancellationToken.None);

        Assert.True(bindResult.Ok, bindResult.Error);

        await plugin.DisconnectAsync(new PluginDisconnectCommand("listener-cascade"), CancellationToken.None);

        var childClosed = await childClosedTask;
        var listenerClosed = await listenerClosedTask;

        Assert.Equal("listener-closed", childClosed.Reason);
        Assert.Equal("local-disconnect", listenerClosed.Reason);
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

    private static async Task<PluginSessionClosedEvent> WaitForSessionClosedAsync(
        NetworkBusAdapterPlugin plugin,
        string sessionId)
    {
        var tcs = new TaskCompletionSource<PluginSessionClosedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, PluginSessionClosedEvent evt)
        {
            if (string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal))
            {
                tcs.TrySetResult(evt);
            }
        }

        plugin.SessionClosed += Handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(tcs.Task, completed);
            return await tcs.Task;
        }
        finally
        {
            plugin.SessionClosed -= Handler;
        }
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
