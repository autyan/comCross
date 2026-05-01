using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ComCross.PluginSdk;

namespace ComCross.Plugins.Network;

public sealed class NetworkBusAdapterPlugin :
    IConnectableBusAdapterPlugin,
    ITransmittableBusAdapterPlugin,
    IPluginActionHandler,
    IPluginUiStateProvider,
    IPluginUiStateEventSource,
    IPluginSessionLifecycleEventSource,
    IPluginSessionStateInitializer,
    IMultiSessionDevicePlugin
{
    private const string PendingResourceKind = PluginResourceKinds.Pending;
    private const string PendingListResourceId = PluginResourceIds.All;
    private const string RejectPendingAction = "network.reject-pending";
    private const string RejectAllPendingAction = "network.reject-all-pending";

    private readonly CancellationTokenSource _cts = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, TcpConnectionSession> _tcpConnections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UdpConnectionSession> _udpConnections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TcpListenerSession> _tcpListeners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UdpListenerSession> _udpListeners = new(StringComparer.Ordinal);

    private readonly Dictionary<string, ISharedMemoryWriter?> _writersBySession = new(StringComparer.Ordinal);
    private string? _singleSessionId;
    private ISharedMemoryWriter? _singleWriter;

    private volatile BackpressureLevel _backpressure;

    private readonly SemaphoreSlim _txGate = new(1, 1); // global best-effort

    private const string TcpParametersSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.tcp.parameters.schema.json";
    private const string TcpConnectUiSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.tcp.connect.ui.schema.json";

    private const string UdpParametersSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.udp.parameters.schema.json";
    private const string UdpConnectUiSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.udp.connect.ui.schema.json";

    private const string TcpServerParametersSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.tcp.server.parameters.schema.json";
    private const string TcpServerConnectUiSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.tcp.server.connect.ui.schema.json";

    private const string UdpListenParametersSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.udp.listen.parameters.schema.json";
    private const string UdpListenConnectUiSchemaResource = "ComCross.Plugins.Network.Resources.Schemas.udp.listen.connect.ui.schema.json";

    public PluginMetadata Metadata { get; } = new()
    {
        Id = "network.adapter",
        Name = "Network Adapter",
        Version = "0.3.2",
        Type = PluginType.BusAdapter,
        Description = "TCP/UDP network bus adapter"
    };

    public event EventHandler<PluginUiStateInvalidatedEvent>? UiStateInvalidated;
    public event EventHandler<PluginSessionClosedEvent>? SessionClosed;

    public Task<PluginSessionStateInitializationResult> InitializeSessionStateAsync(
        PluginSessionStateInitializationContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ParametersJson))
        {
            return Task.FromResult(new PluginSessionStateInitializationResult(true));
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ParametersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(new PluginSessionStateInitializationResult(true));
            }

            var patch = context.CapabilityId switch
            {
                "tcp" => BuildTcpClientSessionPatch(doc.RootElement),
                "udp" => BuildUdpClientSessionPatch(doc.RootElement),
                "tcp.server" or "udp.listen" => BuildScopedSessionPatch(doc.RootElement),
                _ => null
            };

            var storagePatch = new PluginSessionStoragePatch(SchemaVersion: 1);
            return Task.FromResult(new PluginSessionStateInitializationResult(
                true,
                StoragePatch: storagePatch,
                SessionPatch: patch));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PluginSessionStateInitializationResult(false, ex.Message));
        }
    }

    public IReadOnlyList<PluginCapabilityDescriptor> GetCapabilities()
    {
        return new[]
        {
            new PluginCapabilityDescriptor
            {
                Id = "tcp",
                Name = "TCP (Client)",
                Description = "TCP client connection to a remote host",
                Icon = "NetworkIcon",
                JsonSchema = ReadEmbeddedResource(TcpParametersSchemaResource),
                UiSchema = ReadEmbeddedResource(TcpConnectUiSchemaResource),
                DefaultParametersJson = "{}",
                SupportsMultiSession = false,
                SharedMemoryRequest = DefaultSharedMemoryRequest()
            },
            new PluginCapabilityDescriptor
            {
                Id = "udp",
                Name = "UDP (Socket)",
                Description = "UDP socket bound locally and connected to a remote endpoint",
                Icon = "NetworkIcon",
                JsonSchema = ReadEmbeddedResource(UdpParametersSchemaResource),
                UiSchema = ReadEmbeddedResource(UdpConnectUiSchemaResource),
                DefaultParametersJson = "{}",
                SupportsMultiSession = false,
                SharedMemoryRequest = DefaultSharedMemoryRequest()
            },
            new PluginCapabilityDescriptor
            {
                Id = "tcp.server",
                Name = "TCP (Server)",
                Description = "TCP server listener; each accepted client becomes a session",
                Icon = "ServerIcon",
                JsonSchema = ReadEmbeddedResource(TcpServerParametersSchemaResource),
                UiSchema = ReadEmbeddedResource(TcpServerConnectUiSchemaResource),
                DefaultParametersJson = "{\"mode\":\"listen\"}",
                SessionHostModel = SessionHostModel.SharedPerScope,
                SessionHostGroupKeyParameter = "listenerSessionId",
                SupportsMultiSession = true,
                SharedMemoryRequest = DefaultSharedMemoryRequest()
            },
            new PluginCapabilityDescriptor
            {
                Id = "udp.listen",
                Name = "UDP (Listen)",
                Description = "UDP listener; each peer becomes a session",
                Icon = "ServerIcon",
                JsonSchema = ReadEmbeddedResource(UdpListenParametersSchemaResource),
                UiSchema = ReadEmbeddedResource(UdpListenConnectUiSchemaResource),
                DefaultParametersJson = "{\"mode\":\"listen\"}",
                SessionHostModel = SessionHostModel.SharedPerScope,
                SessionHostGroupKeyParameter = "listenerSessionId",
                SupportsMultiSession = true,
                SharedMemoryRequest = DefaultSharedMemoryRequest()
            }
        };
    }

    public PluginUiStateSnapshot GetUiState(PluginUiStateQuery query)
    {
        if (query.SessionId is null)
        {
            var state = new
            {
                defaultParameters = new
                {
                    tcp = new { remoteHost = "127.0.0.1", remotePort = 502, localHost = "", localPort = 0, connectTimeoutMs = 3000 },
                    udp = new { remoteHost = "127.0.0.1", remotePort = 5020, localHost = "", localPort = 0 },
                    tcpServer = new { listenHost = "0.0.0.0", listenPort = 502, backlog = 128 },
                    udpListen = new { listenHost = "0.0.0.0", listenPort = 5020 }
                }
            };

            return new PluginUiStateSnapshot(JsonSerializer.SerializeToElement(state), DateTimeOffset.UtcNow);
        }

        lock (_lock)
        {
            if (_tcpListeners.TryGetValue(query.SessionId, out var tcpListener))
            {
                var pending = tcpListener.GetPendingSnapshot();
                if (string.Equals(query.ResourceKind, PendingResourceKind, StringComparison.Ordinal))
                {
                    return BuildPendingResourceSnapshot("tcp", pending, query.ResourceId);
                }

                var state = new
                {
                    kind = "listener",
                    protocol = "tcp",
                    connected = true,
                    pendingConnections = pending.Select(p => new { id = p.Id, displayName = p.DisplayName }).ToArray()
                };

                return new PluginUiStateSnapshot(JsonSerializer.SerializeToElement(state), DateTimeOffset.UtcNow);
            }

            if (_udpListeners.TryGetValue(query.SessionId, out var udpListener))
            {
                var pending = udpListener.GetPendingSnapshot();
                if (string.Equals(query.ResourceKind, PendingResourceKind, StringComparison.Ordinal))
                {
                    return BuildPendingResourceSnapshot("udp", pending, query.ResourceId);
                }

                var state = new
                {
                    kind = "listener",
                    protocol = "udp",
                    connected = true,
                    pendingConnections = pending.Select(p => new { id = p.Id, displayName = p.DisplayName }).ToArray()
                };

                return new PluginUiStateSnapshot(JsonSerializer.SerializeToElement(state), DateTimeOffset.UtcNow);
            }

            var connected = _tcpConnections.ContainsKey(query.SessionId) || _udpConnections.ContainsKey(query.SessionId);
            var stateFallback = new { kind = "connection", connected };
            return new PluginUiStateSnapshot(JsonSerializer.SerializeToElement(stateFallback), DateTimeOffset.UtcNow);
        }
    }

    public async Task<PluginConnectResult> ConnectAsync(PluginConnectCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.SessionId))
        {
            return new PluginConnectResult(false, "Missing SessionId.");
        }

        if (command.Parameters.ValueKind != JsonValueKind.Object)
        {
            return new PluginConnectResult(false, "Parameters must be an object.");
        }

        return command.CapabilityId switch
        {
            "tcp" => await ConnectTcpAsync(command, cancellationToken),
            "udp" => await ConnectUdpAsync(command, cancellationToken),
            "tcp.server" => await ConnectTcpServerAsync(command, cancellationToken),
            "udp.listen" => await ConnectUdpListenAsync(command, cancellationToken),
            _ => new PluginConnectResult(false, $"Unknown capability: {command.CapabilityId}")
        };
    }

    public Task<PluginCommandResult> DisconnectAsync(PluginDisconnectCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.SessionId))
        {
            CleanupSession(command.SessionId, command.Reason ?? "local-disconnect", remoteInitiated: false);
        }

        return Task.FromResult(new PluginCommandResult(true));
    }

    public async Task<PluginCommandResult> SendAsync(PluginSendCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.SessionId))
        {
            return new PluginCommandResult(false, "Missing SessionId.");
        }

        TcpConnectionSession? tcp;
        UdpConnectionSession? udp;
        lock (_lock)
        {
            _tcpConnections.TryGetValue(command.SessionId, out tcp);
            _udpConnections.TryGetValue(command.SessionId, out udp);
        }

        if (tcp is null && udp is null)
        {
            return new PluginCommandResult(false, "Session is not connected.");
        }

        await _txGate.WaitAsync(cancellationToken);
        try
        {
            var data = command.Data ?? Array.Empty<byte>();

            if (tcp is not null)
            {
                var stream = tcp.Stream;
                if (stream is null)
                {
                    return new PluginCommandResult(false, "TCP stream not available.");
                }

                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                return new PluginCommandResult(true);
            }

            if (udp is not null)
            {
                return await udp.SendAsync(data, cancellationToken);
            }

            return new PluginCommandResult(false, "Unknown session type.");
        }
        catch (Exception ex)
        {
            return new PluginCommandResult(false, ex.Message);
        }
        finally
        {
            _txGate.Release();
        }
    }

    public Task<PluginCommandResult> ExecuteActionAsync(PluginActionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ActionName))
        {
            return Task.FromResult(new PluginCommandResult(false, "Missing action name."));
        }

        return command.ActionName switch
        {
            RejectPendingAction => Task.FromResult(RejectPending(command)),
            RejectAllPendingAction => Task.FromResult(RejectAllPending(command)),
            _ => Task.FromResult(new PluginCommandResult(false, $"Unknown action: {command.ActionName}"))
        };
    }

    public void SetSharedMemoryWriter(ISharedMemoryWriter writer)
    {
        // Single-session host injection.
        _singleWriter = writer;
        if (!string.IsNullOrWhiteSpace(_singleSessionId))
        {
            lock (_lock)
            {
                _writersBySession[_singleSessionId] = writer;
            }
        }
    }

    public void SetSharedMemoryWriter(string sessionId, ISharedMemoryWriter writer)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_lock)
        {
            _writersBySession[sessionId] = writer;
        }
    }

    public void ClearSharedMemoryWriter(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_lock)
        {
            _writersBySession.Remove(sessionId);
        }
    }

    public void SetBackpressureLevel(BackpressureLevel level)
    {
        _backpressure = level;
    }

    private async Task<PluginConnectResult> ConnectTcpAsync(PluginConnectCommand command, CancellationToken cancellationToken)
    {
        var mode = TryReadString(command.Parameters, "mode")?.Trim().ToLowerInvariant();
        if (string.Equals(mode, "bind", StringComparison.Ordinal))
        {
            return await Task.FromResult(BindTcpAcceptedConnection(command));
        }

        var host = TryReadString(command.Parameters, "remoteHost");
        if (string.IsNullOrWhiteSpace(host))
        {
            host = TryReadString(command.Parameters, "host");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return new PluginConnectResult(false, "Missing required parameter: remoteHost");
        }

        if (!TryReadInt(command.Parameters, "remotePort", out var port) &&
            !TryReadInt(command.Parameters, "port", out port))
        {
            return new PluginConnectResult(false, "Missing required parameter: remotePort");
        }

        if (port is < 1 or > 65535)
        {
            return new PluginConnectResult(false, "Invalid remotePort (expected 1..65535)." );
        }

        var localHost = TryReadString(command.Parameters, "localHost");
        var localPort = 0;
        if (TryReadInt(command.Parameters, "localPort", out var lp) && lp >= 0 && lp <= 65535)
        {
            localPort = lp;
        }

        var timeoutMs = 3000;
        if (TryReadInt(command.Parameters, "connectTimeoutMs", out var t) && t > 0)
        {
            timeoutMs = Math.Min(t, 600000);
        }

        try
        {
            var client = CreateTcpClient(localHost, localPort);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            linked.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            await client.ConnectAsync(host, port, linked.Token);

            var stream = client.GetStream();
            var local = client.Client.LocalEndPoint as IPEndPoint;
            var remote = client.Client.RemoteEndPoint as IPEndPoint;
            var subtitle = FormatEndpointPair(local, remote);
            var requestedLocalHost = string.IsNullOrWhiteSpace(localHost) ? string.Empty : localHost.Trim();
            var committed = JsonSerializer.SerializeToElement(new
            {
                host = FormatAddress(remote?.Address) ?? host,
                port = remote?.Port ?? port,
                remoteHost = FormatAddress(remote?.Address) ?? host,
                remotePort = remote?.Port ?? port,
                localHost = requestedLocalHost,
                localPort,
                actualLocalHost = FormatAddress(local?.Address),
                actualLocalPort = local?.Port,
                requestedLocalHost = requestedLocalHost,
                requestedLocalPort = localPort,
                endpoint = subtitle,
                connectTimeoutMs = timeoutMs
            });

            var rxCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var state = new TcpConnectionSession(command.SessionId, client, stream, rxCts, listenerSessionId: null);

            lock (_lock)
            {
                _singleSessionId = command.SessionId;
                if (_singleWriter is not null)
                {
                    _writersBySession[command.SessionId] = _singleWriter;
                }

                _tcpConnections[command.SessionId] = state;
            }

            state.RxLoop = Task.Run(() => TcpReadLoopAsync(command.SessionId, stream, rxCts.Token));

            UiStateInvalidated?.Invoke(this, new PluginUiStateInvalidatedEvent("tcp", SessionId: null, ViewKind: "connect-dialog", Reason: "connected"));
            return new PluginConnectResult(
                true,
                SessionId: command.SessionId,
                CommittedParameters: committed,
                DisplayTitle: "TCP Client",
                DisplaySubtitle: subtitle,
                SessionIcon: "NetworkIcon");
        }
        catch (OperationCanceledException)
        {
            CleanupSession(command.SessionId, "connect-timeout", remoteInitiated: false);
            return new PluginConnectResult(false, "Timeout.");
        }
        catch (Exception ex)
        {
            CleanupSession(command.SessionId, "connect-failed", remoteInitiated: false, error: ex.Message);
            return new PluginConnectResult(false, ex.Message);
        }
    }

    private async Task<PluginConnectResult> ConnectUdpAsync(PluginConnectCommand command, CancellationToken cancellationToken)
    {
        var mode = TryReadString(command.Parameters, "mode")?.Trim().ToLowerInvariant();
        if (string.Equals(mode, "bind", StringComparison.Ordinal))
        {
            return await Task.FromResult(BindUdpPeer(command));
        }

        var remoteHost = TryReadString(command.Parameters, "remoteHost");
        if (string.IsNullOrWhiteSpace(remoteHost))
        {
            return new PluginConnectResult(false, "Missing required parameter: remoteHost");
        }

        if (!TryReadInt(command.Parameters, "remotePort", out var remotePort))
        {
            return new PluginConnectResult(false, "Missing required parameter: remotePort");
        }

        if (remotePort is < 1 or > 65535)
        {
            return new PluginConnectResult(false, "Invalid remotePort (expected 1..65535)." );
        }

        var localHost = TryReadString(command.Parameters, "localHost");
        var localPort = 0;
        if (TryReadInt(command.Parameters, "localPort", out var lp) && lp >= 0 && lp <= 65535)
        {
            localPort = lp;
        }

        try
        {
            var localAddress = ResolveLocalBindAddress(localHost);
            UdpClient udp = localPort > 0 || localAddress is not null
                ? new UdpClient(new IPEndPoint(localAddress ?? IPAddress.Any, localPort))
                : new UdpClient();

            udp.Connect(remoteHost, remotePort);
            var local = udp.Client.LocalEndPoint as IPEndPoint;
            var remote = udp.Client.RemoteEndPoint as IPEndPoint;
            var subtitle = FormatEndpointPair(local, remote);
            var requestedLocalHost = string.IsNullOrWhiteSpace(localHost) ? string.Empty : localHost.Trim();
            var committed = JsonSerializer.SerializeToElement(new
            {
                remoteHost = FormatAddress(remote?.Address) ?? remoteHost,
                remotePort = remote?.Port ?? remotePort,
                localHost = requestedLocalHost,
                localPort,
                actualLocalHost = FormatAddress(local?.Address),
                actualLocalPort = local?.Port,
                requestedLocalHost = requestedLocalHost,
                requestedLocalPort = localPort,
                endpoint = subtitle
            });

            var rxCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var state = new UdpConnectionSession(command.SessionId, udp, remote: null, listenerSessionId: null);
            state.RxLoop = Task.Run(() => UdpReadLoopAsync(command.SessionId, udp, rxCts.Token));
            state.RxCts = rxCts;

            lock (_lock)
            {
                _singleSessionId = command.SessionId;
                if (_singleWriter is not null)
                {
                    _writersBySession[command.SessionId] = _singleWriter;
                }

                _udpConnections[command.SessionId] = state;
            }

            UiStateInvalidated?.Invoke(this, new PluginUiStateInvalidatedEvent("udp", SessionId: null, ViewKind: "connect-dialog", Reason: "connected"));
            await Task.CompletedTask;
            return new PluginConnectResult(
                true,
                SessionId: command.SessionId,
                CommittedParameters: committed,
                DisplayTitle: "UDP Socket",
                DisplaySubtitle: subtitle,
                SessionIcon: "NetworkIcon");
        }
        catch (Exception ex)
        {
            CleanupSession(command.SessionId, "connect-failed", remoteInitiated: false, error: ex.Message);
            return new PluginConnectResult(false, ex.Message);
        }
    }

    private Task<PluginConnectResult> ConnectTcpServerAsync(PluginConnectCommand command, CancellationToken cancellationToken)
    {
        var mode = TryReadString(command.Parameters, "mode")?.Trim().ToLowerInvariant();
        if (string.Equals(mode, "bind", StringComparison.Ordinal) || IsPendingResourceTarget(command))
        {
            return Task.FromResult(BindTcpAcceptedConnection(command));
        }

        // default: listen
        return Task.FromResult(StartTcpListener(command));
    }

    private Task<PluginConnectResult> ConnectUdpListenAsync(PluginConnectCommand command, CancellationToken cancellationToken)
    {
        var mode = TryReadString(command.Parameters, "mode")?.Trim().ToLowerInvariant();
        if (string.Equals(mode, "bind", StringComparison.Ordinal) || IsPendingResourceTarget(command))
        {
            return Task.FromResult(BindUdpPeer(command));
        }

        return Task.FromResult(StartUdpListener(command));
    }

    private PluginConnectResult StartTcpListener(PluginConnectCommand command)
    {
        var listenHost = TryReadString(command.Parameters, "listenHost");
        if (string.IsNullOrWhiteSpace(listenHost))
        {
            listenHost = "0.0.0.0";
        }

        if (!TryReadInt(command.Parameters, "listenPort", out var port) || port is < 1 or > 65535)
        {
            return new PluginConnectResult(false, "Missing or invalid listenPort (expected 1..65535).");
        }

        var backlog = 128;
        if (TryReadInt(command.Parameters, "backlog", out var b) && b > 0)
        {
            backlog = Math.Clamp(b, 1, 8192);
        }

        IPAddress ip;
        if (!IPAddress.TryParse(listenHost, out ip!))
        {
            ip = IPAddress.Any;
        }

        try
        {
            var listener = new TcpListener(new IPEndPoint(ip, port));
            listener.Start(backlog);
            var local = listener.LocalEndpoint as IPEndPoint;
            var subtitle = FormatEndpoint(local);
            var committed = JsonSerializer.SerializeToElement(new
            {
                mode = "listen",
                listenHost = FormatAddress(local?.Address) ?? listenHost,
                listenPort = local?.Port ?? port,
                backlog,
                endpoint = subtitle
            });

            var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var state = new TcpListenerSession(command.SessionId, "tcp.server", listener, acceptCts);

            lock (_lock)
            {
                _tcpListeners[command.SessionId] = state;
            }

            state.AcceptLoop = Task.Run(() => TcpAcceptLoopAsync(state, acceptCts.Token));
            NotifyListenerInvalidated(state.CapabilityId, state.SessionId, "listening");
            return new PluginConnectResult(
                true,
                SessionId: command.SessionId,
                CommittedParameters: committed,
                DisplayTitle: "TCP Listener",
                DisplaySubtitle: subtitle,
                SessionIcon: "ServerIcon",
                ManagedResourceKinds: new[] { PluginResourceKinds.Pending });
        }
        catch (Exception ex)
        {
            return new PluginConnectResult(false, ex.Message);
        }
    }

    private PluginConnectResult StartUdpListener(PluginConnectCommand command)
    {
        var listenHost = TryReadString(command.Parameters, "listenHost");
        if (string.IsNullOrWhiteSpace(listenHost))
        {
            listenHost = "0.0.0.0";
        }

        if (!TryReadInt(command.Parameters, "listenPort", out var port) || port is < 1 or > 65535)
        {
            return new PluginConnectResult(false, "Missing or invalid listenPort (expected 1..65535).");
        }

        IPAddress ip;
        if (!IPAddress.TryParse(listenHost, out ip!))
        {
            ip = IPAddress.Any;
        }

        try
        {
            var udp = new UdpClient(new IPEndPoint(ip, port));
            var rxCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var state = new UdpListenerSession(command.SessionId, "udp.listen", udp, rxCts);

            lock (_lock)
            {
                _udpListeners[command.SessionId] = state;
            }

            state.RxLoop = Task.Run(() => UdpListenLoopAsync(state, rxCts.Token));
            NotifyListenerInvalidated(state.CapabilityId, state.SessionId, "listening");
            var local = udp.Client.LocalEndPoint as IPEndPoint;
            var subtitle = FormatEndpoint(local);
            var committed = JsonSerializer.SerializeToElement(new
            {
                mode = "listen",
                listenHost = FormatAddress(local?.Address) ?? listenHost,
                listenPort = local?.Port ?? port,
                endpoint = subtitle
            });

            return new PluginConnectResult(
                true,
                SessionId: command.SessionId,
                CommittedParameters: committed,
                DisplayTitle: "UDP Listener",
                DisplaySubtitle: subtitle,
                SessionIcon: "ServerIcon",
                ManagedResourceKinds: new[] { PluginResourceKinds.Pending });
        }
        catch (Exception ex)
        {
            return new PluginConnectResult(false, ex.Message);
        }
    }

    private PluginConnectResult BindTcpAcceptedConnection(PluginConnectCommand command)
    {
        var (listenerSessionId, pendingId) = ResolvePendingTarget(command);
        if (string.IsNullOrWhiteSpace(listenerSessionId) || string.IsNullOrWhiteSpace(pendingId))
        {
            return new PluginConnectResult(false, "Missing pending resource target.");
        }

        TcpListenerSession? listener;
        PendingTcpConnection? pending;

        lock (_lock)
        {
            if (!_tcpListeners.TryGetValue(listenerSessionId, out listener))
            {
                return new PluginConnectResult(false, "Listener session not found.");
            }

            pending = listener.TryTakePending(pendingId);
        }

        if (pending is null)
        {
            return new PluginConnectResult(false, "Pending connection not found.");
        }

        try
        {
            var stream = pending.Client.GetStream();
            var local = pending.Client.Client.LocalEndPoint as IPEndPoint;
            var remote = pending.Client.Client.RemoteEndPoint as IPEndPoint;
            var subtitle = FormatEndpointPair(remote, local);
            var committed = JsonSerializer.SerializeToElement(new
            {
                mode = "bind",
                listenerSessionId,
                pendingId,
                host = FormatAddress(remote?.Address),
                port = remote?.Port,
                remoteHost = FormatAddress(remote?.Address),
                remotePort = remote?.Port,
                localHost = FormatAddress(local?.Address),
                localPort = local?.Port,
                endpoint = subtitle
            });
            var rxCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var state = new TcpConnectionSession(command.SessionId, pending.Client, stream, rxCts, listenerSessionId);

            lock (_lock)
            {
                _tcpConnections[command.SessionId] = state;
            }

            state.RxLoop = Task.Run(() => TcpReadLoopAsync(command.SessionId, stream, rxCts.Token));
            NotifyListenerInvalidated(listener!.CapabilityId, listener!.SessionId, "accepted-bound");
            return new PluginConnectResult(
                true,
                SessionId: command.SessionId,
                CommittedParameters: committed,
                DisplayTitle: "Inbound TCP",
                DisplaySubtitle: subtitle,
                ParentSessionId: listenerSessionId,
                SessionIcon: "NetworkIcon",
                CanReconnect: false);
        }
        catch (Exception ex)
        {
            try { pending.Client.Dispose(); } catch { }
            return new PluginConnectResult(false, ex.Message);
        }
    }

    private PluginConnectResult BindUdpPeer(PluginConnectCommand command)
    {
        var (listenerSessionId, pendingId) = ResolvePendingTarget(command);
        if (string.IsNullOrWhiteSpace(listenerSessionId) || string.IsNullOrWhiteSpace(pendingId))
        {
            return new PluginConnectResult(false, "Missing pending resource target.");
        }

        UdpListenerSession? listener;
        UdpPeerPending? peer;

        lock (_lock)
        {
            if (!_udpListeners.TryGetValue(listenerSessionId, out listener))
            {
                return new PluginConnectResult(false, "Listener session not found.");
            }

            peer = listener.TryBindPeer(pendingId, command.SessionId);
            if (peer is null)
            {
                return new PluginConnectResult(false, "Pending peer not found.");
            }

            _udpConnections[command.SessionId] = new UdpConnectionSession(command.SessionId, listener.Udp, peer.RemoteEndPoint, listenerSessionId);
        }

        // Flush buffered datagrams best-effort.
        if (peer.BufferedDatagrams is { Count: > 0 })
        {
            foreach (var datagram in peer.BufferedDatagrams)
            {
                TryWriteFrame(command.SessionId, datagram);
            }
        }

        NotifyListenerInvalidated(listener!.CapabilityId, listener!.SessionId, "peer-bound");
        var subtitle = FormatEndpoint(peer.RemoteEndPoint);
        var committed = JsonSerializer.SerializeToElement(new
        {
            mode = "bind",
            listenerSessionId,
            pendingId,
            remoteHost = FormatAddress(peer.RemoteEndPoint.Address),
            remotePort = peer.RemoteEndPoint.Port,
            endpoint = subtitle
        });

        return new PluginConnectResult(
            true,
            SessionId: command.SessionId,
            CommittedParameters: committed,
            DisplayTitle: "Inbound UDP",
            DisplaySubtitle: subtitle,
            ParentSessionId: listenerSessionId,
            SessionIcon: "NetworkIcon",
            CanReconnect: false);
    }

    private PluginCommandResult RejectPending(PluginActionCommand command)
    {
        var listenerSessionId = command.SessionId;
        var pendingId = TryReadString(command.Parameters, "pendingId");
        if (string.IsNullOrWhiteSpace(listenerSessionId) || string.IsNullOrWhiteSpace(pendingId))
        {
            return new PluginCommandResult(false, "Missing listener session or pendingId.");
        }

        lock (_lock)
        {
            if (_tcpListeners.TryGetValue(listenerSessionId, out var tcpListener))
            {
                if (!tcpListener.TryRejectPending(pendingId))
                {
                    return new PluginCommandResult(false, "Pending connection not found.");
                }

                NotifyListenerInvalidated(tcpListener.CapabilityId, tcpListener.SessionId, "pending-rejected");
                return new PluginCommandResult(true);
            }

            if (_udpListeners.TryGetValue(listenerSessionId, out var udpListener))
            {
                if (!udpListener.TryRejectPending(pendingId))
                {
                    return new PluginCommandResult(false, "Pending peer not found.");
                }

                NotifyListenerInvalidated(udpListener.CapabilityId, udpListener.SessionId, "pending-rejected");
                return new PluginCommandResult(true);
            }
        }

        return new PluginCommandResult(false, "Listener session not found.");
    }

    private PluginCommandResult RejectAllPending(PluginActionCommand command)
    {
        var listenerSessionId = command.SessionId;
        if (string.IsNullOrWhiteSpace(listenerSessionId))
        {
            return new PluginCommandResult(false, "Missing listener session.");
        }

        lock (_lock)
        {
            if (_tcpListeners.TryGetValue(listenerSessionId, out var tcpListener))
            {
                tcpListener.RejectAllPending();
                NotifyListenerInvalidated(tcpListener.CapabilityId, tcpListener.SessionId, "pending-cleared");
                return new PluginCommandResult(true);
            }

            if (_udpListeners.TryGetValue(listenerSessionId, out var udpListener))
            {
                udpListener.RejectAllPending();
                NotifyListenerInvalidated(udpListener.CapabilityId, udpListener.SessionId, "pending-cleared");
                return new PluginCommandResult(true);
            }
        }

        return new PluginCommandResult(false, "Listener session not found.");
    }

    private async Task TcpReadLoopAsync(string sessionId, NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        var reason = "transport-closed";
        string? error = null;
        var remoteInitiated = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                reason = "transport-error";
                error = ex.Message;
                break;
            }

            if (read <= 0)
            {
                reason = "remote-eof";
                remoteInitiated = true;
                break;
            }

            if (_backpressure == BackpressureLevel.High)
            {
                await Task.Delay(5, cancellationToken);
                continue;
            }

            var payload = read == buffer.Length ? buffer.ToArray() : buffer.AsSpan(0, read).ToArray();
            TryWriteFrame(sessionId, payload);
        }

        CleanupSession(sessionId, reason, remoteInitiated, error);
    }

    private async Task UdpReadLoopAsync(string sessionId, UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_backpressure == BackpressureLevel.High)
                {
                    await Task.Delay(5, cancellationToken);
                    continue;
                }

                var result = await udp.ReceiveAsync(cancellationToken);
                var payload = result.Buffer ?? Array.Empty<byte>();
                if (payload.Length > 0)
                {
                    TryWriteFrame(sessionId, payload);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }

        CleanupSession(sessionId, "transport-closed", remoteInitiated: false);
    }

    private void CleanupSession(string sessionId, string? reason = null, bool remoteInitiated = false, string? error = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        TcpConnectionSession? tcp;
        UdpConnectionSession? udp;
        TcpListenerSession? tcpListener;
        UdpListenerSession? udpListener;
        List<string> childSessionIds = new();
        var removed = false;

        lock (_lock)
        {
            if (string.Equals(_singleSessionId, sessionId, StringComparison.Ordinal))
            {
                _singleSessionId = null;
            }

            _writersBySession.Remove(sessionId);

            _tcpConnections.TryGetValue(sessionId, out tcp);
            removed |= _tcpConnections.Remove(sessionId);

            _udpConnections.TryGetValue(sessionId, out udp);
            removed |= _udpConnections.Remove(sessionId);

            _tcpListeners.TryGetValue(sessionId, out tcpListener);
            removed |= _tcpListeners.Remove(sessionId);

            _udpListeners.TryGetValue(sessionId, out udpListener);
            removed |= _udpListeners.Remove(sessionId);

            if (tcpListener is not null || udpListener is not null)
            {
                childSessionIds.AddRange(_tcpConnections.Values
                    .Where(s => string.Equals(s.ListenerSessionId, sessionId, StringComparison.Ordinal))
                    .Select(s => s.SessionId));
                childSessionIds.AddRange(_udpConnections.Values
                    .Where(s => string.Equals(s.ListenerSessionId, sessionId, StringComparison.Ordinal))
                    .Select(s => s.SessionId));
            }
        }

        tcp?.Dispose();
        udp?.Dispose();
        tcpListener?.Dispose();
        udpListener?.Dispose();

        foreach (var childSessionId in childSessionIds.Distinct(StringComparer.Ordinal))
        {
            CleanupSession(childSessionId, "listener-closed", remoteInitiated: false);
        }

        if (removed)
        {
            SessionClosed?.Invoke(this, new PluginSessionClosedEvent(
                sessionId,
                reason ?? "closed",
                remoteInitiated,
                error));
        }

        UiStateInvalidated?.Invoke(this, new PluginUiStateInvalidatedEvent("network", SessionId: null, ViewKind: "connect-dialog", Reason: "disconnected"));
    }

    private void NotifyListenerInvalidated(string capabilityId, string sessionId, string reason)
    {
        try
        {
            UiStateInvalidated?.Invoke(this, new PluginUiStateInvalidatedEvent(
                capabilityId,
                SessionId: sessionId,
                ViewKind: "listener",
                Reason: reason,
                ResourceKind: PendingResourceKind,
                ResourceId: PendingListResourceId));
        }
        catch
        {
        }
    }

    private static PluginUiStateSnapshot BuildPendingResourceSnapshot(
        string protocol,
        IReadOnlyList<(string Id, string DisplayName)> pending,
        string? resourceId)
    {
        if (!string.IsNullOrWhiteSpace(resourceId)
            && !string.Equals(resourceId, PendingListResourceId, StringComparison.Ordinal))
        {
            var selected = pending.FirstOrDefault(p => string.Equals(p.Id, resourceId, StringComparison.Ordinal));
            var itemState = new
            {
                kind = "pending-item",
                protocol,
                resourceKind = PendingResourceKind,
                item = string.IsNullOrWhiteSpace(selected.Id)
                    ? null
                    : BuildPendingResourceItem(protocol, selected.Id, selected.DisplayName)
            };

            return new PluginUiStateSnapshot(JsonSerializer.SerializeToElement(itemState), DateTimeOffset.UtcNow);
        }

        var state = new PluginResourceListState(
            "pending-list",
            PendingResourceKind,
            pending.Select(p => BuildPendingResourceItem(protocol, p.Id, p.DisplayName)).ToArray(),
            new[]
            {
                new PluginResourceActionDescriptor(
                    PluginResourceActionIds.RejectAll,
                    PluginResourceActionKinds.ExecuteAction,
                    LabelKey: "network.session.manager.clearAllPending",
                    ActionName: RejectAllPendingAction,
                    Parameters: JsonSerializer.SerializeToElement(new { }))
            });

        return new PluginUiStateSnapshot(JsonSerializer.SerializeToElement(state), DateTimeOffset.UtcNow);
    }

    private static PluginManagedResourceItem BuildPendingResourceItem(
        string protocol,
        string pendingId,
        string displayName)
        => new(
            pendingId,
            displayName,
            new[]
            {
                new PluginResourceActionDescriptor(
                    PluginResourceActionIds.Accept,
                    PluginResourceActionKinds.ConnectScopedResource,
                    LabelKey: string.Equals(protocol, "udp", StringComparison.Ordinal)
                        ? "network.session.manager.bindPending"
                        : "network.session.manager.acceptPending",
                    Parameters: JsonSerializer.SerializeToElement(BuildPendingConnectionParameters(protocol, displayName)),
                    SessionName: displayName),
                new PluginResourceActionDescriptor(
                    PluginResourceActionIds.Reject,
                    PluginResourceActionKinds.ExecuteAction,
                    LabelKey: "network.session.manager.rejectPending",
                    ActionName: RejectPendingAction,
                    Parameters: JsonSerializer.SerializeToElement(new { pendingId }))
            });

    private static object BuildPendingConnectionParameters(string protocol, string displayName)
    {
        var endpoint = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        if (!TryParseHostPort(displayName, out var host, out var port))
        {
            return new { endpoint };
        }

        return string.Equals(protocol, "udp", StringComparison.Ordinal)
            ? new { remoteHost = host, remotePort = port, endpoint }
            : new { host, port, endpoint };
    }

    private static bool TryParseHostPort(string? displayName, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var lastColon = displayName.LastIndexOf(':');
        if (lastColon <= 0 || lastColon >= displayName.Length - 1)
        {
            return false;
        }

        var h = displayName[..lastColon].Trim();
        var p = displayName[(lastColon + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(h) || !int.TryParse(p, out var parsed) || parsed is < 1 or > 65535)
        {
            return false;
        }

        host = h;
        port = parsed;
        return true;
    }

    private static (string? ListenerSessionId, string? PendingId) ResolvePendingTarget(PluginConnectCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.ScopeSessionId)
            && string.Equals(command.ResourceKind, PendingResourceKind, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(command.ResourceId))
        {
            return (command.ScopeSessionId, command.ResourceId);
        }

        return (
            TryReadString(command.Parameters, "listenerSessionId"),
            TryReadString(command.Parameters, "pendingId"));
    }

    private static string? FormatAddress(IPAddress? address)
        => address is null
            ? null
            : address.IsIPv4MappedToIPv6
                ? address.MapToIPv4().ToString()
                : address.ToString();

    private static IPAddress? ResolveLocalBindAddress(string? localHost)
    {
        if (string.IsNullOrWhiteSpace(localHost))
        {
            return null;
        }

        if (IPAddress.TryParse(localHost.Trim(), out var address))
        {
            return address;
        }

        return null;
    }

    private static TcpClient CreateTcpClient(string? localHost, int localPort)
    {
        var localAddress = ResolveLocalBindAddress(localHost);
        if (localAddress is null && localPort <= 0)
        {
            return new TcpClient();
        }

        return new TcpClient(new IPEndPoint(localAddress ?? IPAddress.Any, localPort));
    }

    private static string FormatEndpoint(IPEndPoint? endpoint)
        => endpoint is null ? string.Empty : $"{FormatAddress(endpoint.Address)}:{endpoint.Port}";

    private static string FormatEndpointPair(IPEndPoint? left, IPEndPoint? right)
    {
        var l = FormatEndpoint(left);
        var r = FormatEndpoint(right);
        if (string.IsNullOrWhiteSpace(l))
        {
            return r;
        }

        if (string.IsNullOrWhiteSpace(r))
        {
            return l;
        }

        return $"{l} -> {r}";
    }

    private static bool IsPendingResourceTarget(PluginConnectCommand command)
        => !string.IsNullOrWhiteSpace(command.ScopeSessionId)
            && string.Equals(command.ResourceKind, PendingResourceKind, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(command.ResourceId);

    private void TryWriteFrame(string sessionId, byte[] payload)
    {
        if (_backpressure == BackpressureLevel.High)
        {
            return;
        }

        ISharedMemoryWriter? writer;
        lock (_lock)
        {
            _writersBySession.TryGetValue(sessionId, out writer);
        }

        writer?.TryWriteFrame(payload, out _);
    }

    private async Task TcpAcceptLoopAsync(TcpListenerSession listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.Listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (client is null)
            {
                continue;
            }

            var remote = client.Client.RemoteEndPoint as IPEndPoint;
            var pendingId = Guid.NewGuid().ToString("N");
            var display = remote is null ? "tcp" : $"{remote.Address}:{remote.Port}";

            lock (_lock)
            {
                // Listener might have been removed.
                if (!_tcpListeners.TryGetValue(listener.SessionId, out var current) || !ReferenceEquals(current, listener))
                {
                    try { client.Dispose(); } catch { }
                    continue;
                }

                listener.AddPending(new PendingTcpConnection(pendingId, client, display));
            }

            NotifyListenerInvalidated(listener.CapabilityId, listener.SessionId, "pending");
        }
    }

    private async Task UdpListenLoopAsync(UdpListenerSession listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await listener.Udp.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (result.Buffer is not { Length: > 0 })
            {
                continue;
            }

            var remote = result.RemoteEndPoint;
            var key = listener.GetPeerKey(remote);

            string? boundSessionId;
            lock (_lock)
            {
                boundSessionId = listener.TryGetBoundSessionId(key);
                if (boundSessionId is null)
                {
                    listener.EnsurePendingPeer(key, remote, result.Buffer);
                }
            }

            if (boundSessionId is not null)
            {
                TryWriteFrame(boundSessionId, result.Buffer);
                continue;
            }

            NotifyListenerInvalidated(listener.CapabilityId, listener.SessionId, "pending");
        }
    }

    private sealed class TcpConnectionSession : IDisposable
    {
        public TcpConnectionSession(
            string sessionId,
            TcpClient client,
            NetworkStream stream,
            CancellationTokenSource rxCts,
            string? listenerSessionId)
        {
            SessionId = sessionId;
            Client = client;
            Stream = stream;
            RxCts = rxCts;
            ListenerSessionId = listenerSessionId;
        }

        public string SessionId { get; }
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public CancellationTokenSource RxCts { get; }
        public string? ListenerSessionId { get; }
        public Task? RxLoop { get; set; }

        public void Dispose()
        {
            try { RxCts.Cancel(); } catch { }
            try { RxCts.Dispose(); } catch { }
            try { Stream.Dispose(); } catch { }
            try { Client.Dispose(); } catch { }
        }
    }

    private sealed class UdpConnectionSession : IDisposable
    {
        public UdpConnectionSession(string sessionId, UdpClient udp, IPEndPoint? remote, string? listenerSessionId)
        {
            SessionId = sessionId;
            Udp = udp;
            Remote = remote;
            ListenerSessionId = listenerSessionId;
        }

        public string SessionId { get; }
        public UdpClient Udp { get; }
        public IPEndPoint? Remote { get; }
        public string? ListenerSessionId { get; }

        public CancellationTokenSource? RxCts { get; set; }
        public Task? RxLoop { get; set; }

        public async Task<PluginCommandResult> SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            try
            {
                if (Remote is null)
                {
                    // Connected UDP client.
                    await Udp.SendAsync(data, cancellationToken);
                    return new PluginCommandResult(true);
                }

                // Listener-owned UDP socket: send to remote endpoint.
                await Udp.SendAsync(data, Remote, cancellationToken);
                return new PluginCommandResult(true);
            }
            catch (Exception ex)
            {
                return new PluginCommandResult(false, ex.Message);
            }
        }

        public void Dispose()
        {
            try { RxCts?.Cancel(); } catch { }
            try { RxCts?.Dispose(); } catch { }

            // Only dispose UDP client if this is a standalone connected client.
            if (Remote is null)
            {
                try { Udp.Dispose(); } catch { }
            }
        }
    }

    private sealed class TcpListenerSession : IDisposable
    {
        private readonly Dictionary<string, PendingTcpConnection> _pending = new(StringComparer.Ordinal);

        public TcpListenerSession(string sessionId, string capabilityId, TcpListener listener, CancellationTokenSource acceptCts)
        {
            SessionId = sessionId;
            CapabilityId = capabilityId;
            Listener = listener;
            AcceptCts = acceptCts;
        }

        public string SessionId { get; }
        public string CapabilityId { get; }
        public TcpListener Listener { get; }
        public CancellationTokenSource AcceptCts { get; }
        public Task? AcceptLoop { get; set; }

        public void AddPending(PendingTcpConnection pending) => _pending[pending.Id] = pending;

        public PendingTcpConnection? TryTakePending(string pendingId)
        {
            if (_pending.TryGetValue(pendingId, out var pending))
            {
                _pending.Remove(pendingId);
                return pending;
            }

            return null;
        }

        public bool TryRejectPending(string pendingId)
        {
            if (!_pending.TryGetValue(pendingId, out var pending))
            {
                return false;
            }

            _pending.Remove(pendingId);
            try { pending.Client.Dispose(); } catch { }
            return true;
        }

        public int RejectAllPending()
        {
            var pending = _pending.Values.ToArray();
            _pending.Clear();

            foreach (var item in pending)
            {
                try { item.Client.Dispose(); } catch { }
            }

            return pending.Length;
        }

        public IReadOnlyList<(string Id, string DisplayName)> GetPendingSnapshot()
            => _pending.Values.Select(p => (p.Id, p.DisplayName)).ToArray();

        public void Dispose()
        {
            try { AcceptCts.Cancel(); } catch { }
            try { AcceptCts.Dispose(); } catch { }

            try { Listener.Stop(); } catch { }

            foreach (var p in _pending.Values)
            {
                try { p.Client.Dispose(); } catch { }
            }

            _pending.Clear();
        }
    }

    private sealed record PendingTcpConnection(string Id, TcpClient Client, string DisplayName);

    private sealed class UdpListenerSession : IDisposable
    {
        private readonly Dictionary<string, UdpPeerPending> _pendingById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pendingIdByPeerKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _boundSessionByPeerKey = new(StringComparer.Ordinal);

        public UdpListenerSession(string sessionId, string capabilityId, UdpClient udp, CancellationTokenSource rxCts)
        {
            SessionId = sessionId;
            CapabilityId = capabilityId;
            Udp = udp;
            RxCts = rxCts;
        }

        public string SessionId { get; }
        public string CapabilityId { get; }
        public UdpClient Udp { get; }
        public CancellationTokenSource RxCts { get; }
        public Task? RxLoop { get; set; }

        public string GetPeerKey(IPEndPoint ep) => $"{ep.Address}:{ep.Port}";

        public string? TryGetBoundSessionId(string peerKey)
            => _boundSessionByPeerKey.TryGetValue(peerKey, out var sid) ? sid : null;

        public void EnsurePendingPeer(string peerKey, IPEndPoint remote, byte[] firstDatagram)
        {
            if (!_pendingIdByPeerKey.TryGetValue(peerKey, out var pendingId))
            {
                pendingId = Guid.NewGuid().ToString("N");
                _pendingIdByPeerKey[peerKey] = pendingId;
                _pendingById[pendingId] = new UdpPeerPending(
                    pendingId,
                    remote,
                    displayName: $"udp {remote.Address}:{remote.Port}");
            }

            if (_pendingById.TryGetValue(pendingId, out var pending))
            {
                pending.Append(firstDatagram);
            }
        }

        public UdpPeerPending? TryBindPeer(string pendingId, string sessionId)
        {
            if (!_pendingById.TryGetValue(pendingId, out var pending))
            {
                return null;
            }

            var key = GetPeerKey(pending.RemoteEndPoint);
            _boundSessionByPeerKey[key] = sessionId;

            _pendingById.Remove(pendingId);
            _pendingIdByPeerKey.Remove(key);
            return pending;
        }

        public bool TryRejectPending(string pendingId)
        {
            if (!_pendingById.TryGetValue(pendingId, out var pending))
            {
                return false;
            }

            var key = GetPeerKey(pending.RemoteEndPoint);
            _pendingById.Remove(pendingId);
            _pendingIdByPeerKey.Remove(key);
            return true;
        }

        public int RejectAllPending()
        {
            var count = _pendingById.Count;
            _pendingById.Clear();
            _pendingIdByPeerKey.Clear();
            return count;
        }

        public IReadOnlyList<(string Id, string DisplayName)> GetPendingSnapshot()
            => _pendingById.Values.Select(p => (p.Id, p.DisplayName)).ToArray();

        public void Dispose()
        {
            try { RxCts.Cancel(); } catch { }
            try { RxCts.Dispose(); } catch { }
            try { Udp.Dispose(); } catch { }
            _pendingById.Clear();
            _pendingIdByPeerKey.Clear();
            _boundSessionByPeerKey.Clear();
        }
    }

    private sealed class UdpPeerPending
    {
        private const int MaxBufferedBytes = 256 * 1024;
        private int _bufferedBytes;
        private readonly List<byte[]> _buffer = new();

        public UdpPeerPending(string id, IPEndPoint remoteEndPoint, string displayName)
        {
            Id = id;
            RemoteEndPoint = remoteEndPoint;
            DisplayName = displayName;
        }

        public string Id { get; }
        public IPEndPoint RemoteEndPoint { get; }
        public string DisplayName { get; }

        public IReadOnlyList<byte[]> BufferedDatagrams => _buffer;

        public void Append(byte[] datagram)
        {
            if (datagram.Length <= 0)
            {
                return;
            }

            if (_bufferedBytes + datagram.Length > MaxBufferedBytes)
            {
                return;
            }

            _buffer.Add(datagram);
            _bufferedBytes += datagram.Length;
        }
    }

    private static SharedMemoryRequest DefaultSharedMemoryRequest()
    {
        return new SharedMemoryRequest
        {
            MinBytes = 64 * 1024,
            PreferredBytes = 256 * 1024,
            MaxBytes = 4 * 1024 * 1024,
            SupportsWriterSwitch = true,
            GrowthStepBytes = 256 * 1024
        };
    }

    private static PluginSessionMetadataPatch? BuildTcpClientSessionPatch(JsonElement parameters)
    {
        var changed = false;
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);

        CopyOrMapString(parameters, normalized, "remoteHost", "host", ref changed);
        CopyOrMapInt(parameters, normalized, "remotePort", "port", ref changed);
        CopyRequestedOrCanonicalString(parameters, normalized, "localHost", "requestedLocalHost", ref changed);
        CopyRequestedOrCanonicalInt(parameters, normalized, "localPort", "requestedLocalPort", ref changed);
        CopyIfPresent(parameters, normalized, "connectTimeoutMs");
        CopyIfPresent(parameters, normalized, "sessionName");

        return changed
            ? new PluginSessionMetadataPatch(ParametersJson: JsonSerializer.Serialize(normalized))
            : null;
    }

    private static PluginSessionMetadataPatch? BuildUdpClientSessionPatch(JsonElement parameters)
    {
        var changed = false;
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);

        CopyOrMapString(parameters, normalized, "remoteHost", "host", ref changed);
        CopyOrMapInt(parameters, normalized, "remotePort", "port", ref changed);
        CopyRequestedOrCanonicalString(parameters, normalized, "localHost", "requestedLocalHost", ref changed);
        CopyRequestedOrCanonicalInt(parameters, normalized, "localPort", "requestedLocalPort", ref changed);
        CopyIfPresent(parameters, normalized, "sessionName");

        return changed
            ? new PluginSessionMetadataPatch(ParametersJson: JsonSerializer.Serialize(normalized))
            : null;
    }

    private static PluginSessionMetadataPatch? BuildScopedSessionPatch(JsonElement parameters)
    {
        var hasParent = !string.IsNullOrWhiteSpace(TryReadString(parameters, "listenerSessionId"));
        return hasParent ? new PluginSessionMetadataPatch(CanReconnect: false) : null;
    }

    private static void CopyOrMapString(
        JsonElement source,
        Dictionary<string, object?> target,
        string canonicalKey,
        string fallbackKey,
        ref bool changed)
    {
        var value = TryReadString(source, canonicalKey);
        if (value is null)
        {
            value = TryReadString(source, fallbackKey);
            if (value is not null)
            {
                changed = true;
            }
        }

        if (value is not null)
        {
            target[canonicalKey] = value;
        }
    }

    private static void CopyOrMapInt(
        JsonElement source,
        Dictionary<string, object?> target,
        string canonicalKey,
        string fallbackKey,
        ref bool changed)
    {
        if (TryReadInt(source, canonicalKey, out var value))
        {
            target[canonicalKey] = value;
            return;
        }

        if (TryReadInt(source, fallbackKey, out value))
        {
            target[canonicalKey] = value;
            changed = true;
        }
    }

    private static void CopyRequestedOrCanonicalString(
        JsonElement source,
        Dictionary<string, object?> target,
        string canonicalKey,
        string requestedKey,
        ref bool changed)
    {
        if (TryReadString(source, requestedKey) is { } requested)
        {
            target[canonicalKey] = requested;
            if (!string.Equals(TryReadString(source, canonicalKey), requested, StringComparison.Ordinal))
            {
                changed = true;
            }

            return;
        }

        if (TryReadString(source, canonicalKey) is { } canonical)
        {
            target[canonicalKey] = canonical;
        }
    }

    private static void CopyRequestedOrCanonicalInt(
        JsonElement source,
        Dictionary<string, object?> target,
        string canonicalKey,
        string requestedKey,
        ref bool changed)
    {
        if (TryReadInt(source, requestedKey, out var requested))
        {
            target[canonicalKey] = requested;
            if (!TryReadInt(source, canonicalKey, out var canonical) || canonical != requested)
            {
                changed = true;
            }

            return;
        }

        if (TryReadInt(source, canonicalKey, out var value))
        {
            target[canonicalKey] = value;
        }
    }

    private static void CopyIfPresent(JsonElement source, Dictionary<string, object?> target, string key)
    {
        if (!source.TryGetProperty(key, out var prop))
        {
            return;
        }

        target[key] = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number when prop.TryGetInt32(out var i) => i,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => prop.Clone()
        };
    }

    private static string ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(NetworkBusAdapterPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Missing embedded resource: {resourceName}. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool TryReadInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
        {
            return true;
        }

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
        {
            return true;
        }

        return false;
    }

    private static string? TryReadString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }
}
