using System.IO.Pipes;
using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ComCross.Platform.SharedMemory;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

var argsMap = ParseArgs(args);

if (!argsMap.TryGetValue("--pipe", out var pipeName) ||
    !argsMap.TryGetValue("--plugin", out var pluginPath) ||
    !argsMap.TryGetValue("--entry", out var entryPoint))
{
    Console.Error.WriteLine("Missing required arguments: --pipe --plugin --entry");
    return 2;
}

argsMap.TryGetValue("--role", out var roleRaw);
var role = NormalizeRole(roleRaw,
#if SESSION_HOST
    defaultRole: "session"
#else
    defaultRole: "ui"
#endif
);

#if SESSION_HOST
if (!string.Equals(role, "session", StringComparison.Ordinal))
{
    Console.Error.WriteLine("This executable is Session Host only (role=session).");
    return 2;
}
#else
if (!string.Equals(role, "ui", StringComparison.Ordinal))
{
    Console.Error.WriteLine("This executable is UI Host only (role=ui).");
    return 2;
}
#endif

argsMap.TryGetValue("--session-id", out var fixedSessionId);
#if SESSION_HOST
// Note: --session-id is optional.
// - When provided, the host will only accept that specific session id.
// - When omitted, the host may accept multiple sessions (only if the plugin supports multi-session).
#else
fixedSessionId = null;
#endif

argsMap.TryGetValue("--event-pipe", out var eventPipeName);
argsMap.TryGetValue("--host-token", out var hostToken);

_ = StartParentMonitorIfRequested(argsMap);

var state = new HostState(entryPoint, pluginPath, fixedSessionId);
state.SetHostToken(hostToken);
state.TryLoadPlugin();

var eventSink = new HostEventSink(eventPipeName);
if (string.Equals(role, "ui", StringComparison.Ordinal))
{
    state.SetUiStateEventSink(eventSink.PublishUiStateInvalidated);
}
else
{
    // Session Host must not emit UI invalidation events.
    state.SetUiStateEventSink(_ => { });
}
state.SetSessionRegisteredSink(eventSink.PublishSessionRegistered);

eventSink.PublishHostRegistered(hostToken);

var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
await server.WaitForConnectionAsync();

using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
{
    AutoFlush = true
};

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null)
    {
        break;
    }

    PluginHostRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<PluginHostRequest>(line, jsonOptions);
    }
    catch (Exception ex)
    {
        var response = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, ex.Message);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
        continue;
    }

    if (request is null)
    {
        var response = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, "Invalid request.");
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
        continue;
    }

    if (!IsAllowed(role, request.Type))
    {
        var response = new PluginHostResponse(request.Id, false, $"Message '{request.Type}' is not supported by role '{role}'.");
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
        continue;
    }

    var responseMessage = request.Type switch
    {
        PluginHostMessageTypes.Ping => HandlePing(request, state),
        PluginHostMessageTypes.Notify => HandleNotify(request, state),
        PluginHostMessageTypes.GetCapabilities => HandleGetCapabilities(request, state, jsonOptions),
        PluginHostMessageTypes.Connect => HandleConnect(request, state, jsonOptions),
        PluginHostMessageTypes.Disconnect => HandleDisconnect(request, state, jsonOptions),
        PluginHostMessageTypes.GetUiState => HandleGetUiState(request, state, jsonOptions),
        PluginHostMessageTypes.ApplySharedMemorySegment => HandleApplySharedMemorySegment(request, state, jsonOptions),
        PluginHostMessageTypes.SetBackpressure => HandleSetBackpressure(request, state, jsonOptions),
        PluginHostMessageTypes.SendData => HandleSendData(request, state, jsonOptions),
        PluginHostMessageTypes.LanguageChanged => HandleLanguageChanged(request, state, jsonOptions),
        PluginHostMessageTypes.Shutdown => HandleShutdown(request),
        _ => new PluginHostResponse(request.Id, false, $"Unknown request type: {request.Type}")
    };

    await writer.WriteLineAsync(JsonSerializer.Serialize(responseMessage, jsonOptions));

    if (request.Type == PluginHostMessageTypes.Shutdown)
    {
        break;
    }
}

eventSink.Dispose();

return 0;

static string NormalizeRole(string? role, string defaultRole)
    => string.IsNullOrWhiteSpace(role) ? defaultRole : role.Trim().ToLowerInvariant();

static bool IsAllowed(string role, string messageType)
{
    // Note: role-gating is an architectural boundary.
    // UI Host: capabilities + UI state only.
    // Session Host: session lifecycle + shared memory + backpressure.
    if (string.Equals(role, "ui", StringComparison.Ordinal))
    {
        return messageType is PluginHostMessageTypes.Ping
            or PluginHostMessageTypes.Notify
            or PluginHostMessageTypes.GetCapabilities
            or PluginHostMessageTypes.GetUiState
            or PluginHostMessageTypes.LanguageChanged
            or PluginHostMessageTypes.Shutdown;
    }

    // session
    return messageType is PluginHostMessageTypes.Ping
        or PluginHostMessageTypes.Notify
        or PluginHostMessageTypes.GetCapabilities
        or PluginHostMessageTypes.ApplySharedMemorySegment
        or PluginHostMessageTypes.Connect
        or PluginHostMessageTypes.Disconnect
        or PluginHostMessageTypes.SetBackpressure
        or PluginHostMessageTypes.SendData
        or PluginHostMessageTypes.Shutdown;
}

static Task? StartParentMonitorIfRequested(Dictionary<string, string> argsMap)
{
    if (!argsMap.TryGetValue("--parent-pid", out var pidText) ||
        !int.TryParse(pidText, out var parentPid) ||
        parentPid <= 0)
    {
        return null;
    }

    DateTimeOffset? expectedStartUtc = null;
    if (argsMap.TryGetValue("--parent-start-utc", out var startText) &&
        DateTimeOffset.TryParse(startText, out var parsed))
    {
        expectedStartUtc = parsed;
    }

    return Task.Run(async () =>
    {
        try
        {
            var parent = Process.GetProcessById(parentPid);

            if (expectedStartUtc is not null)
            {
                try
                {
                    var actual = parent.StartTime.ToUniversalTime();
                    var delta = (actual - expectedStartUtc.Value.UtcDateTime).Duration();
                    if (delta > TimeSpan.FromSeconds(2))
                    {
                        Environment.Exit(0);
                        return;
                    }
                }
                catch
                {
                }
            }

            await parent.WaitForExitAsync();
        }
        catch
        {
        }

        Environment.Exit(0);
    });
}

static PluginHostResponse HandlePing(PluginHostRequest request, HostState state)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    return new PluginHostResponse(request.Id, true);
}

static PluginHostResponse HandleNotify(PluginHostRequest request, HostState state)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Notification is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing notification payload.");
    }

    if (!PluginNotificationTypes.IsKnownGlobal(request.Notification.Type))
    {
        return new PluginHostResponse(request.Id, false, $"Unknown notification type: {request.Notification.Type}");
    }

    if (state.Instance is not IPluginNotificationSubscriber subscriber)
    {
        return new PluginHostResponse(request.Id, true);
    }

    try
    {
        subscriber.OnNotification(request.Notification);
        return new PluginHostResponse(request.Id, true);
    }
    catch (Exception ex)
    {
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleShutdown(PluginHostRequest request)
{
    return new PluginHostResponse(request.Id, true);
}

static PluginHostResponse HandleLanguageChanged(PluginHostRequest request, HostState state, JsonSerializerOptions jsonOptions)
{
    // Forward-compatibility hook: allow the main process to notify plugin host about UI language changes.
    // Currently a no-op acknowledgement.
    return new PluginHostResponse(request.Id, true);
}

static PluginHostResponse HandleConnect(PluginHostRequest request, HostState state, JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Payload is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing connect payload.");
    }

    PluginHostConnectPayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<PluginHostConnectPayload>(jsonOptions);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, $"Invalid connect payload: {ex.Message}");
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.CapabilityId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid connect payload: missing CapabilityId.");
    }

    if (string.IsNullOrWhiteSpace(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid connect payload: missing SessionId.");
    }

    // If capabilities are declared, validate capability id and parameters using lite schema validation.
    if (state.Instance is IPluginCapabilityProvider provider)
    {
        PluginCapabilityDescriptor? capability = null;
        try
        {
            capability = provider.GetCapabilities()?.FirstOrDefault(c => string.Equals(c.Id, payload.CapabilityId, StringComparison.Ordinal));
        }
        catch
        {
            // If capability enumeration fails, treat as invalid and let restart logic handle in the call path.
        }

        if (capability is null)
        {
            return new PluginHostResponse(request.Id, false, $"Unknown capability: {payload.CapabilityId}");
        }

        if (!string.IsNullOrWhiteSpace(capability.JsonSchema))
        {
            if (!JsonSchemaLiteValidator.TryParseSchema(capability.JsonSchema, out var schema, out var parseError))
            {
                return new PluginHostResponse(request.Id, false, $"Invalid capability schema: {parseError}");
            }

            if (!JsonSchemaLiteValidator.TryValidate(schema, payload.Parameters, out var validateError))
            {
                return new PluginHostResponse(request.Id, false, $"Parameters validation failed: {validateError}");
            }
        }
    }

    if (state.Instance is not IConnectableBusAdapterPlugin connectable)
    {
        return new PluginHostResponse(request.Id, false, "Plugin does not support connect.");
    }

    if (!state.TryBeginSession(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Another session is already active.");
    }

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = connectable.ConnectAsync(
            new PluginConnectCommand(payload.CapabilityId, payload.Parameters, payload.SessionId),
            cts.Token).GetAwaiter().GetResult();

        if (result.Ok)
        {
            // ADR-010 closure: under single-session-per-runtime model, plugin must echo the SessionId.
            if (string.IsNullOrWhiteSpace(result.SessionId))
            {
                state.EndSession(payload.SessionId);
                return new PluginHostResponse(request.Id, false, "Protocol violation: plugin did not return SessionId.");
            }

            if (!string.Equals(result.SessionId, payload.SessionId, StringComparison.Ordinal))
            {
                state.EndSession(payload.SessionId);
                return new PluginHostResponse(request.Id, false, "Protocol violation: plugin returned mismatched SessionId.");
            }

            state.PublishSessionRegistered(payload.SessionId);
        }
        else
        {
            state.EndSession(payload.SessionId);
        }

        var json = JsonSerializer.Serialize(result, jsonOptions);
        var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
        return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: resultPayload);
    }
    catch (Exception ex)
    {
        state.EndSession(payload.SessionId);
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleDisconnect(PluginHostRequest request, HostState state, JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (state.Instance is not IConnectableBusAdapterPlugin connectable)
    {
        return new PluginHostResponse(request.Id, false, "Plugin does not support disconnect.");
    }

    if (request.Payload is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing disconnect payload.");
    }

    PluginHostDisconnectPayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<PluginHostDisconnectPayload>(jsonOptions);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, $"Invalid disconnect payload: {ex.Message}");
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid disconnect payload: missing SessionId.");
    }

    if (!state.IsActiveSession(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Session is not active.");
    }

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = connectable.DisconnectAsync(
            new PluginDisconnectCommand(payload.SessionId, payload.Reason),
            cts.Token).GetAwaiter().GetResult();

        state.EndSession(payload.SessionId);

        var json = JsonSerializer.Serialize(result, jsonOptions);
        var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
        return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: resultPayload);
    }
    catch (Exception ex)
    {
        state.EndSession(payload.SessionId);
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleApplySharedMemorySegment(PluginHostRequest request, HostState state, JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Payload is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing apply-shared-memory-segment payload.");
    }

    PluginHostApplySharedMemorySegmentPayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<PluginHostApplySharedMemorySegmentPayload>(jsonOptions);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, $"Invalid apply-shared-memory-segment payload: {ex.Message}");
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId) || payload.Descriptor is null)
    {
        return new PluginHostResponse(request.Id, false, "Invalid apply-shared-memory-segment payload.");
    }

    if (!state.IsActiveSession(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Session is not active.");
    }

    try
    {
        var ok = state.TryApplySharedMemoryWriter(payload.SessionId, payload.Descriptor);
        var result = ok
            ? new SegmentUpgradeResult(true, GrantedBytes: payload.Descriptor.SegmentSize)
            : new SegmentUpgradeResult(false, "Plugin does not support shared memory writer injection.");

        var json = JsonSerializer.Serialize(result, jsonOptions);
        var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
        return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: resultPayload);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, ex.Message);
    }
}

static PluginHostResponse HandleSetBackpressure(PluginHostRequest request, HostState state, JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Payload is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing set-backpressure payload.");
    }

    PluginHostSetBackpressurePayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<PluginHostSetBackpressurePayload>(jsonOptions);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, $"Invalid set-backpressure payload: {ex.Message}");
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid set-backpressure payload: missing SessionId.");
    }

    if (!state.IsActiveSession(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Session is not active.");
    }

    if (state.Instance is not IDevicePlugin plugin)
    {
        return new PluginHostResponse(request.Id, false, "Plugin does not support backpressure.");
    }

    try
    {
        plugin.SetBackpressureLevel(payload.Level);
        return new PluginHostResponse(request.Id, true);
    }
    catch (Exception ex)
    {
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleSendData(PluginHostRequest request, HostState state, JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Payload is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing send-data payload.");
    }

    PluginHostSendDataPayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<PluginHostSendDataPayload>(jsonOptions);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, $"Invalid send-data payload: {ex.Message}");
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid send-data payload: missing SessionId.");
    }

    if (!state.IsActiveSession(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Session is not active.");
    }

    if (state.Instance is not ITransmittableBusAdapterPlugin tx)
    {
        return new PluginHostResponse(request.Id, false, "Plugin does not support TX.");
    }

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = tx.SendAsync(
            new PluginSendCommand(payload.SessionId, payload.Data ?? Array.Empty<byte>()),
            cts.Token).GetAwaiter().GetResult();

        var json = JsonSerializer.Serialize(result, jsonOptions);
        var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
        return new PluginHostResponse(request.Id, result.Ok, result.Error, Payload: resultPayload);
    }
    catch (Exception ex)
    {
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleGetCapabilities(
    PluginHostRequest request,
    HostState state,
    JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (state.Instance is not IPluginCapabilityProvider provider)
    {
        // Capability declaration is optional; treat as empty.
        var empty = JsonDocument.Parse("[]").RootElement.Clone();
        return new PluginHostResponse(request.Id, true, Payload: empty);
    }

    try
    {
        var capabilities = provider.GetCapabilities() ?? Array.Empty<PluginCapabilityDescriptor>();
        var json = JsonSerializer.Serialize(capabilities, jsonOptions);
        var payload = JsonDocument.Parse(json).RootElement.Clone();
        return new PluginHostResponse(request.Id, true, Payload: payload);
    }
    catch (Exception ex)
    {
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleGetUiState(
    PluginHostRequest request,
    HostState state,
    JsonSerializerOptions jsonOptions)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Payload is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing ui-state payload.");
    }

    PluginHostGetUiStatePayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<PluginHostGetUiStatePayload>(jsonOptions);
    }
    catch (Exception ex)
    {
        return new PluginHostResponse(request.Id, false, $"Invalid ui-state payload: {ex.Message}");
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.CapabilityId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid ui-state payload: missing CapabilityId.");
    }

    // ADR-010 closure: sessionless is represented by null only.
    // Empty/whitespace session ids are invalid and must not bypass gating.
    if (payload.SessionId is not null && string.IsNullOrWhiteSpace(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Invalid ui-state payload: invalid SessionId.");
    }

    if (state.Instance is not IPluginUiStateProvider provider)
    {
        return new PluginHostResponse(request.Id, false, "Plugin does not support ui-state.");
    }

    if (payload.SessionId is not null && !state.IsActiveSession(payload.SessionId))
    {
        return new PluginHostResponse(request.Id, false, "Session is not active.");
    }

    try
    {
        var snapshot = provider.GetUiState(new PluginUiStateQuery(payload.CapabilityId, payload.SessionId, payload.ViewKind, payload.ViewInstanceId));
        var json = JsonSerializer.Serialize(snapshot, jsonOptions);
        var resultPayload = JsonDocument.Parse(json).RootElement.Clone();
        return new PluginHostResponse(request.Id, true, Payload: resultPayload);
    }
    catch (Exception ex)
    {
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        map[args[i]] = args[i + 1];
        i++;
    }

    return map;
}

sealed class HostEventSink : IDisposable
{
    private readonly string? _pipeName;
    private readonly Channel<string> _queue;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public HostEventSink(string? pipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? null : pipeName;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        if (_pipeName is not null)
        {
            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptAndWriteLoopAsync(_cts.Token));
        }
    }

    public void PublishUiStateInvalidated(PluginUiStateInvalidatedEvent evt)
    {
        if (_pipeName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(evt.CapabilityId))
        {
            return;
        }

        // ADR-010 closure: sessionless is represented by null only.
        if (evt.SessionId is not null && string.IsNullOrWhiteSpace(evt.SessionId))
        {
            return;
        }

        try
        {
            var payloadJson = JsonSerializer.Serialize(evt, _jsonOptions);
            var payload = JsonDocument.Parse(payloadJson).RootElement.Clone();
            var envelope = new PluginHostEvent(PluginHostEventTypes.UiStateInvalidated, payload);
            var line = JsonSerializer.Serialize(envelope, _jsonOptions);
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // best-effort
        }
    }

    public void PublishHostRegistered(string? token)
    {
        if (_pipeName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            var payload = new PluginHostRegisteredEvent(token, Process.GetCurrentProcess().Id);
            var payloadJson = JsonSerializer.Serialize(payload, _jsonOptions);
            var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
            var envelope = new PluginHostEvent(PluginHostEventTypes.HostRegistered, payloadElement);
            var line = JsonSerializer.Serialize(envelope, _jsonOptions);
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // best-effort
        }
    }

    public void PublishSessionRegistered(string token, string sessionId)
    {
        if (_pipeName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var payload = new PluginHostSessionRegisteredEvent(token, Process.GetCurrentProcess().Id, sessionId);
            var payloadJson = JsonSerializer.Serialize(payload, _jsonOptions);
            var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
            var envelope = new PluginHostEvent(PluginHostEventTypes.SessionRegistered, payloadElement);
            var line = JsonSerializer.Serialize(envelope, _jsonOptions);
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // best-effort
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        _cts = null;
        _acceptLoop = null;
    }

    private async Task AcceptAndWriteLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName!,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                await using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };

                while (!cancellationToken.IsCancellationRequested && await _queue.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (_queue.Reader.TryRead(out var line))
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // If client disconnects or pipe breaks, loop back and accept again.
                await Task.Delay(200, cancellationToken);
            }
        }
    }
}

sealed class HostState
{
    private readonly string _entryPoint;
    private readonly string _pluginPath;
    private readonly string? _fixedSessionId;
    private PluginLoadContext? _loadContext;

    public HostState(string entryPoint, string pluginPath, string? fixedSessionId)
    {
        _entryPoint = entryPoint;
        _pluginPath = pluginPath;
        _fixedSessionId = string.IsNullOrWhiteSpace(fixedSessionId) ? null : fixedSessionId;
    }

    public object? Instance { get; private set; }
    public string? LoadError { get; private set; }
    public bool IsLoaded => Instance != null && LoadError is null;

    public string? HostToken { get; private set; }

    private Action<PluginUiStateInvalidatedEvent>? _uiStateEventSink;
    private IPluginUiStateEventSource? _uiStateEventSource;

    private Action<string, string>? _sessionRegisteredSink;

    private readonly object _writerLock = new();
    private SwitchableSharedMemoryWriter? _switchableWriter;
    private IDisposable? _currentWriterHandle;

    private readonly object _multiWriterLock = new();
    private readonly Dictionary<string, IDisposable> _writerHandlesBySession = new(StringComparer.Ordinal);

    private readonly object _sessionLock = new();
    private string? _activeSessionId;
    private readonly HashSet<string> _activeSessions = new(StringComparer.Ordinal);

    private bool SupportsMultiSession => Instance is IMultiSessionDevicePlugin;

    public void SetHostToken(string? token)
    {
        HostToken = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public void SetSessionRegisteredSink(Action<string, string> sink)
    {
        _sessionRegisteredSink = sink;
    }

    public void PublishSessionRegistered(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(HostToken))
            {
                return;
            }

            _sessionRegisteredSink?.Invoke(HostToken, sessionId);
        }
        catch
        {
        }
    }

    public void TryLoadPlugin()
    {
        try
        {
            UnloadContext();
            _loadContext = new PluginLoadContext(_pluginPath);
            var assembly = _loadContext.LoadFromAssemblyPath(Path.GetFullPath(_pluginPath));
            var type = assembly.GetType(_entryPoint, throwOnError: true);
            Instance = Activator.CreateInstance(type!);
            LoadError = null;
            ResetWriterState();
            BindUiStateEvents();
        }
        catch (Exception ex)
        {
            Instance = null;
            LoadError = ex.Message;
            ResetWriterState();
            BindUiStateEvents();
        }
    }

    private void UnloadContext()
    {
        var ctx = _loadContext;
        _loadContext = null;

        if (ctx is null)
        {
            return;
        }

        try
        {
            ctx.Unload();
        }
        catch
        {
        }
    }

    public bool TryBeginSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (_fixedSessionId is not null && !string.Equals(_fixedSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_sessionLock)
        {
            if (SupportsMultiSession)
            {
                _activeSessions.Add(sessionId);
                return true;
            }

            if (string.IsNullOrWhiteSpace(_activeSessionId))
            {
                _activeSessionId = sessionId;
                return true;
            }

            return string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal);
        }
    }

    public void EndSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_fixedSessionId is not null && !string.Equals(_fixedSessionId, sessionId, StringComparison.Ordinal))
        {
            return;
        }

        if (SupportsMultiSession)
        {
            lock (_sessionLock)
            {
                _activeSessions.Remove(sessionId);
            }

            // Dispose per-session writer handle (if any) and clear it from plugin.
            if (Instance is IMultiSessionDevicePlugin multi)
            {
                try
                {
                    multi.ClearSharedMemoryWriter(sessionId);
                }
                catch
                {
                }
            }

            IDisposable? handle = null;
            lock (_multiWriterLock)
            {
                if (_writerHandlesBySession.TryGetValue(sessionId, out var existing))
                {
                    handle = existing;
                    _writerHandlesBySession.Remove(sessionId);
                }
            }

            try
            {
                handle?.Dispose();
            }
            catch
            {
            }

            return;
        }

        var shouldReset = false;
        lock (_sessionLock)
        {
            if (string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
            {
                _activeSessionId = null;
                shouldReset = true;
            }
        }

        if (shouldReset)
        {
            ResetWriterState();
        }
    }

    public bool IsActiveSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (_fixedSessionId is not null && !string.Equals(_fixedSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_sessionLock)
        {
            if (SupportsMultiSession)
            {
                return _activeSessions.Contains(sessionId);
            }

            return !string.IsNullOrWhiteSpace(_activeSessionId)
                && string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal);
        }
    }

    private void ResetWriterState()
    {
        // Multi-session writers: dispose all per-session handles.
        Dictionary<string, IDisposable> handles;
        lock (_multiWriterLock)
        {
            handles = new Dictionary<string, IDisposable>(_writerHandlesBySession, StringComparer.Ordinal);
            _writerHandlesBySession.Clear();
        }

        foreach (var kvp in handles)
        {
            try
            {
                if (Instance is IMultiSessionDevicePlugin multi)
                {
                    multi.ClearSharedMemoryWriter(kvp.Key);
                }
            }
            catch
            {
            }

            try
            {
                kvp.Value.Dispose();
            }
            catch
            {
            }
        }

        lock (_writerLock)
        {
            _switchableWriter = null;
            try
            {
                _currentWriterHandle?.Dispose();
            }
            catch
            {
            }
            _currentWriterHandle = null;
        }

        lock (_sessionLock)
        {
            _activeSessionId = null;
            _activeSessions.Clear();
        }
    }

    public bool TryApplySharedMemoryWriter(string sessionId, SharedMemorySegmentDescriptor descriptor)
    {
        if (Instance is not IDevicePlugin plugin)
        {
            return false;
        }

        // Multi-session capable plugins receive a dedicated writer per session.
        if (plugin is IMultiSessionDevicePlugin multi)
        {
            var mapFactory = new SharedMemoryMapFactory();
            var useFileBackedOnUnix = !string.IsNullOrWhiteSpace(descriptor.UnixFilePath);
            var handle = mapFactory.Create(new SharedMemoryMapOptions(
                Name: descriptor.MapName,
                CapacityBytes: descriptor.MapCapacityBytes,
                UnixFilePath: descriptor.UnixFilePath,
                UseFileBackedOnUnix: useFileBackedOnUnix,
                DeleteUnixFileOnDispose: false));

            var accessor = handle.Map.CreateViewAccessor(
                descriptor.SegmentOffset,
                descriptor.SegmentSize,
                MemoryMappedFileAccess.ReadWrite);

            var segment = new SessionSegment(sessionId, accessor, descriptor.SegmentSize, logger: null, initializeHeader: false);
            var nextWriter = new MappedSessionSegmentWriter(segment, handle);

            IDisposable? previous = null;
            lock (_multiWriterLock)
            {
                if (_writerHandlesBySession.TryGetValue(sessionId, out var existing))
                {
                    previous = existing;
                }

                _writerHandlesBySession[sessionId] = nextWriter;
            }

            try
            {
                multi.SetSharedMemoryWriter(sessionId, nextWriter);
            }
            catch
            {
                // If injection fails, rollback the handle.
                try { nextWriter.Dispose(); } catch { }
                return false;
            }

            try
            {
                previous?.Dispose();
            }
            catch
            {
            }

            return true;
        }

        lock (_writerLock)
        {
            var mapFactory = new SharedMemoryMapFactory();
            var useFileBackedOnUnix = !string.IsNullOrWhiteSpace(descriptor.UnixFilePath);
            var handle = mapFactory.Create(new SharedMemoryMapOptions(
                Name: descriptor.MapName,
                CapacityBytes: descriptor.MapCapacityBytes,
                UnixFilePath: descriptor.UnixFilePath,
                UseFileBackedOnUnix: useFileBackedOnUnix,
                DeleteUnixFileOnDispose: false));

            var accessor = handle.Map.CreateViewAccessor(
                descriptor.SegmentOffset,
                descriptor.SegmentSize,
                MemoryMappedFileAccess.ReadWrite);

            var segment = new SessionSegment(sessionId, accessor, descriptor.SegmentSize, logger: null, initializeHeader: false);
            var nextWriter = new MappedSessionSegmentWriter(segment, handle);

            if (_switchableWriter is null)
            {
                _switchableWriter = new SwitchableSharedMemoryWriter(nextWriter);
                plugin.SetSharedMemoryWriter(_switchableWriter);
                _currentWriterHandle = nextWriter;
                return true;
            }

            var previous = _currentWriterHandle;
            _currentWriterHandle = nextWriter;
            _switchableWriter.SwitchTo(nextWriter);

            try
            {
                previous?.Dispose();
            }
            catch
            {
            }

            return true;
        }
    }

    private sealed class MappedSessionSegmentWriter : ISharedMemoryWriter, IDisposable
    {
        private readonly SessionSegment _segment;
        private readonly SharedMemoryMapHandle _handle;

        public MappedSessionSegmentWriter(SessionSegment segment, SharedMemoryMapHandle handle)
        {
            _segment = segment;
            _handle = handle;
        }

        public bool TryWriteFrame(ReadOnlySpan<byte> data, out long frameId) => _segment.TryWriteFrame(data, out frameId);

        public long GetFreeSpace() => _segment.GetFreeSpace();

        public double GetUsageRatio() => _segment.GetUsageRatio();

        public void Dispose()
        {
            try
            {
                _segment.Dispose();
            }
            catch
            {
            }

            try
            {
                _handle.Dispose();
            }
            catch
            {
            }
        }
    }

    public bool TryRestart()
    {
        TryLoadPlugin();
        return IsLoaded;
    }

    public void SetUiStateEventSink(Action<PluginUiStateInvalidatedEvent> sink)
    {
        _uiStateEventSink = sink;
        BindUiStateEvents();
    }

    private void BindUiStateEvents()
    {
        if (_uiStateEventSource is not null)
        {
            _uiStateEventSource.UiStateInvalidated -= OnUiStateInvalidated;
        }

        _uiStateEventSource = Instance as IPluginUiStateEventSource;
        if (_uiStateEventSource is not null)
        {
            _uiStateEventSource.UiStateInvalidated += OnUiStateInvalidated;
        }
    }

    private void OnUiStateInvalidated(object? sender, PluginUiStateInvalidatedEvent evt)
    {
        try
        {
            _uiStateEventSink?.Invoke(evt);
        }
        catch
        {
        }
    }
}

sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.Ordinal)
    {
        // Shared between host and plugin; must come from the default context to keep type identity.
        "ComCross.PluginSdk",
        "ComCross.Shared",
        "ComCross.Platform",
    };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath)
        : base($"ComCross-PluginHost-{Path.GetFileNameWithoutExtension(pluginAssemblyPath)}-{Guid.NewGuid():N}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(Path.GetFullPath(pluginAssemblyPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            return null;
        }

        if (SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            return null;
        }

        return LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is null)
        {
            return IntPtr.Zero;
        }

        return LoadUnmanagedDllFromPath(path);
    }
}
