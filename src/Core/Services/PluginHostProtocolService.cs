using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

    /// <summary>
    /// Core-side helpers for standardized PluginHost IPC commands.
    ///
    /// v0.4+ process model:
    /// - UI Host (per plugin) serves sessionless UI state queries.
    /// - Session Host (per session or per listener group) serves connect/disconnect and can also serve
    ///   session-scoped UI state queries (e.g. listener pending peers).
    /// </summary>
public sealed class PluginHostProtocolService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SharedMemorySessionService _sharedMemorySessionService;
    private readonly SessionHostRuntimeService _sessionHostRuntimeService;
    private readonly PluginUiConfigService _pluginUiConfigService;
    private readonly ILogger<PluginHostProtocolService> _logger;

    public PluginHostProtocolService(
        SharedMemorySessionService sharedMemorySessionService,
        SessionHostRuntimeService sessionHostRuntimeService,
        PluginUiConfigService pluginUiConfigService,
        ILogger<PluginHostProtocolService> logger)
    {
        _sharedMemorySessionService = sharedMemorySessionService ?? throw new ArgumentNullException(nameof(sharedMemorySessionService));
        _sessionHostRuntimeService = sessionHostRuntimeService ?? throw new ArgumentNullException(nameof(sessionHostRuntimeService));
        _pluginUiConfigService = pluginUiConfigService ?? throw new ArgumentNullException(nameof(pluginUiConfigService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(bool Ok, string? Error, PluginSessionStateInitializationResult? Result)> InitializeSessionStateAsync(
        PluginRuntime runtime,
        PluginSessionStateInitializationContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return (false, "Plugin not loaded.", null);
        }

        var payload = JsonSerializer.SerializeToElement(context, JsonOptions);
        var response = await runtime.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.InitializeSessionState, Payload: payload),
            timeout);

        if (response is null)
        {
            return (false, "Plugin host unavailable.", null);
        }

        if (!response.Ok)
        {
            return (false, response.Error, null);
        }

        if (response.Payload is null)
        {
            return (true, null, new PluginSessionStateInitializationResult(true));
        }

        try
        {
            var result = response.Payload.Value.Deserialize<PluginSessionStateInitializationResult>(JsonOptions);
            return result is null
                ? (false, "Invalid session initialization payload.", null)
                : (true, null, result);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid session initialization payload: {ex.Message}", null);
        }
    }

    public async Task<(bool Ok, string? Error)> SetBackpressureAsync(
        PluginRuntime runtime,
        string sessionId,
        BackpressureLevel level,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return (false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (false, "Invalid sessionId.");
        }

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            return (false, "Session host not running.");
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostSetBackpressurePayload(sessionId, level),
            JsonOptions);

        var response = await sessionHost.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.SetBackpressure, SessionId: sessionId, Payload: payload),
            timeout);

        if (response is null)
        {
            return (false, "Session host unavailable.");
        }

        return response.Ok ? (true, null) : (false, response.Error);
    }

    public async Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
        PluginRuntime runtime,
        string capabilityId,
        string? sessionId,
        string? viewKind,
        string? viewInstanceId,
        string? resourceKind,
        string? resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return (false, "Plugin not loaded.", null);
        }

        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return (false, "Missing capabilityId.", null);
        }

        // ADR-010 closure: sessionless is represented by null only.
        // Empty/whitespace session ids are invalid and must not bypass validation.
        if (sessionId is not null && string.IsNullOrWhiteSpace(sessionId))
        {
            return (false, "Invalid sessionId.", null);
        }

        var settings = await _pluginUiConfigService.BuildSettingsSnapshotAsync(runtime.Info, cancellationToken);

        // If sessionId is provided, query the corresponding session host.
        // Session-scoped UI state is maintained by the session host for listener-style plugins.
        var payload = JsonSerializer.SerializeToElement(
            new PluginHostGetUiStatePayload(
                capabilityId,
                sessionId,
                viewKind,
                viewInstanceId,
                PluginId: null,
                ResourceKind: resourceKind,
                ResourceId: resourceId,
                Settings: settings),
            JsonOptions);

        PluginHostResponse? response;
        if (sessionId is null)
        {
            response = await runtime.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.GetUiState, Payload: payload),
                timeout);
        }
        else
        {
            var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
            if (sessionHost is null)
            {
                return (false, "Session host not running.", null);
            }

            response = await sessionHost.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.GetUiState, SessionId: sessionId, Payload: payload),
                timeout);
        }

        if (response is null)
        {
            return (false, "Plugin host unavailable.", null);
        }

        if (!response.Ok)
        {
            return (false, response.Error, null);
        }

        if (response.Payload is null)
        {
            return (false, "Missing ui-state payload.", null);
        }

        try
        {
            var snapshot = response.Payload.Value.Deserialize<PluginUiStateSnapshot>(JsonOptions);
            return snapshot is null
                ? (false, "Invalid ui-state payload.", null)
                : (true, null, snapshot);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid ui-state payload: {ex.Message}", null);
        }
    }

    public async Task<(bool Ok, string? Error, PluginCommandResult? Result)> ExecuteActionAsync(
        PluginRuntime runtime,
        string actionName,
        string? sessionId,
        JsonElement? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return (false, "Plugin not loaded.", null);
        }

        if (string.IsNullOrWhiteSpace(actionName))
        {
            return (false, "Missing actionName.", null);
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostExecuteActionPayload(actionName, parameters),
            JsonOptions);

        PluginHostResponse? response;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            response = await runtime.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.ExecuteAction, Payload: payload),
                timeout);
        }
        else
        {
            var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
            if (sessionHost is null)
            {
                return (false, "Session host not running.", null);
            }

            response = await sessionHost.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.ExecuteAction, SessionId: sessionId, Payload: payload),
                timeout);
        }

        if (response is null)
        {
            return (false, "Plugin host unavailable.", null);
        }

        if (!response.Ok)
        {
            return (false, response.Error, null);
        }

        if (response.Payload is null)
        {
            return (true, null, new PluginCommandResult(true));
        }

        try
        {
            var result = response.Payload.Value.Deserialize<PluginCommandResult>(JsonOptions);
            return result is null
                ? (false, "Invalid execute-action payload.", null)
                : (true, null, result);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid execute-action payload: {ex.Message}", null);
        }
    }

    public async Task<PluginConnectResult> ConnectAsync(
        PluginRuntime runtime,
        string capabilityId,
        JsonElement parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return new PluginConnectResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return new PluginConnectResult(false, "Missing capabilityId.");
        }

        var validateError = ValidateParameters(runtime, capabilityId, parameters);
        if (!string.IsNullOrWhiteSpace(validateError))
        {
            return new PluginConnectResult(false, validateError);
        }

        var sessionId = $"session-{Guid.NewGuid():N}";

        _logger.LogInformation(
            "Connecting plugin (session host). PluginId={PluginId}, CapabilityId={CapabilityId}, SessionId={SessionId}",
            runtime.Info.Manifest.Id,
            capabilityId,
            sessionId);

        var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
        var sessionHostModel = ResolveSessionHostModel(capability);
        var supportsMultiSession = sessionHostModel is not SessionHostModel.DedicatedPerSession;
        var multiSessionGroupId = ComputeMultiSessionGroupId(sessionId, sessionHostModel, capability, parameters);

        SessionHostRuntime sessionHost;
        try
        {
            sessionHost = await _sessionHostRuntimeService.EnsureStartedAsync(
                runtime.Info,
                sessionId,
                capabilityId,
                supportsMultiSession,
                multiSessionGroupId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return new PluginConnectResult(false, $"Failed to start session host: {ex.Message}");
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostConnectPayload(capabilityId, parameters, sessionId),
            JsonOptions);

        PluginHostResponse? response;
        try
        {
            response = await sessionHost.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Connect, SessionId: sessionId, Payload: payload),
                timeout);
        }
        catch (Exception ex)
        {
            await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: "connect-exception");
            return new PluginConnectResult(false, $"Connect failed: {ex.Message}", SessionId: sessionId);
        }

        if (response is null)
        {
            await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: "connect-no-response");
            return new PluginConnectResult(false, "Session host unavailable.", SessionId: sessionId);
        }

        PluginConnectResult? result = null;
        if (response.Payload is not null)
        {
            try
            {
                result = response.Payload.Value.Deserialize<PluginConnectResult>(JsonOptions);
            }
            catch
            {
                // Ignore and fall back to response.Ok/Error.
            }
        }

        result ??= new PluginConnectResult(response.Ok, response.Error, SessionId: sessionId);
        result = result with { SessionId = sessionId };

        if (!result.Ok)
        {
            await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: "connect-failed");
            return result;
        }

        // ADR-010 closure: if shared memory is declared by the capability, initialize it as part of connect.
        var sharedMemoryRequest = capability?.SharedMemoryRequest;
        if (sharedMemoryRequest is not null)
        {
            var requestedBytes = sharedMemoryRequest.PreferredBytes is > 0
                ? sharedMemoryRequest.PreferredBytes
                : sharedMemoryRequest.MinBytes is > 0
                    ? sharedMemoryRequest.MinBytes
                    : 256 * 1024;

            var upgraded = await AllocateAndApplySharedMemorySegmentAsync(
                runtime,
                sessionId,
                requestedBytes,
                timeout,
                cancellationToken);

            if (!upgraded.Ok)
            {
                _logger.LogWarning(
                    "Shared memory init failed on connect. PluginId={PluginId}, SessionId={SessionId}, Error={Error}",
                    runtime.Info.Manifest.Id,
                    sessionId,
                    upgraded.Error);

                await DisconnectAsync(
                    runtime,
                    sessionId: sessionId,
                    reason: $"shared-memory-init-failed: {upgraded.Error}",
                    timeout: timeout,
                    cancellationToken: cancellationToken);

                var error = string.IsNullOrWhiteSpace(upgraded.Error) ? "Unknown error." : upgraded.Error;
                return new PluginConnectResult(false, $"Shared memory init failed: {error}", SessionId: sessionId);
            }

            if (sharedMemoryRequest.MinBytes > 0
                && upgraded.GrantedBytes is int granted
                && granted < sharedMemoryRequest.MinBytes)
            {
                _logger.LogWarning(
                    "Shared memory under MinBytes on connect. PluginId={PluginId}, SessionId={SessionId}, GrantedBytes={GrantedBytes}, MinBytes={MinBytes}",
                    runtime.Info.Manifest.Id,
                    sessionId,
                    granted,
                    sharedMemoryRequest.MinBytes);

                await DisconnectAsync(
                    runtime,
                    sessionId: sessionId,
                    reason: $"shared-memory-under-min: granted={granted}, min={sharedMemoryRequest.MinBytes}",
                    timeout: timeout,
                    cancellationToken: cancellationToken);

                return new PluginConnectResult(
                    false,
                    $"Shared memory allocation under MinBytes: granted={granted}, min={sharedMemoryRequest.MinBytes}.",
                    SessionId: sessionId);
            }
        }

        return result;
    }

    public async Task<PluginConnectResult> ConnectSessionAsync(
        PluginRuntime runtime,
        string capabilityId,
        string sessionId,
        JsonElement parameters,
        string? scopeSessionId,
        string? resourceKind,
        string? resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return new PluginConnectResult(false, "Plugin not loaded.", SessionId: sessionId);
        }

        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return new PluginConnectResult(false, "Missing capabilityId.", SessionId: sessionId);
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new PluginConnectResult(false, "Missing sessionId.");
        }

        var validateError = ValidateParameters(runtime, capabilityId, parameters);
        if (!string.IsNullOrWhiteSpace(validateError))
        {
            return new PluginConnectResult(false, validateError, SessionId: sessionId);
        }

        var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
        var sessionHostModel = ResolveSessionHostModel(capability);
        var supportsMultiSession = sessionHostModel is not SessionHostModel.DedicatedPerSession;
        var multiSessionGroupId = ComputeMultiSessionGroupId(sessionId, sessionHostModel, capability, parameters, scopeSessionId);

        SessionHostRuntime sessionHost;
        try
        {
            sessionHost = await _sessionHostRuntimeService.EnsureStartedAsync(
                runtime.Info,
                sessionId,
                capabilityId,
                supportsMultiSession,
                multiSessionGroupId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return new PluginConnectResult(false, $"Failed to start session host: {ex.Message}", SessionId: sessionId);
        }

        try
        {
            var payload = JsonSerializer.SerializeToElement(
                new PluginHostConnectPayload(
                    capabilityId,
                    parameters,
                    sessionId,
                    scopeSessionId,
                    resourceKind,
                    resourceId),
                JsonOptions);

            var response = await sessionHost.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Connect, SessionId: sessionId, Payload: payload),
                timeout);

            if (response is null)
            {
                return new PluginConnectResult(false, "Session host unavailable.", SessionId: sessionId);
            }

            PluginConnectResult? result = null;
            if (response.Payload is not null)
            {
                try
                {
                    result = response.Payload.Value.Deserialize<PluginConnectResult>(JsonOptions);
                }
                catch
                {
                }
            }

            result ??= new PluginConnectResult(response.Ok, response.Error, SessionId: sessionId);
            result = result with { SessionId = sessionId };
            if (!result.Ok)
            {
                return result;
            }

            var sharedMemoryRequest = capability?.SharedMemoryRequest;
            if (sharedMemoryRequest is not null)
            {
                var requestedBytes = sharedMemoryRequest.PreferredBytes is > 0
                    ? sharedMemoryRequest.PreferredBytes
                    : sharedMemoryRequest.MinBytes is > 0
                        ? sharedMemoryRequest.MinBytes
                        : 256 * 1024;

                var upgraded = await AllocateAndApplySharedMemorySegmentAsync(
                    runtime,
                    sessionId,
                    requestedBytes,
                    timeout,
                    cancellationToken);

                if (!upgraded.Ok)
                {
                    await DisconnectAsync(
                        runtime,
                        sessionId,
                        $"shared-memory-init-failed: {upgraded.Error}",
                        timeout,
                        cancellationToken);

                    var error = string.IsNullOrWhiteSpace(upgraded.Error) ? "Unknown error." : upgraded.Error;
                    return new PluginConnectResult(false, $"Shared memory init failed: {error}", SessionId: sessionId);
                }

                if (sharedMemoryRequest.MinBytes > 0
                    && upgraded.GrantedBytes is int granted
                    && granted < sharedMemoryRequest.MinBytes)
                {
                    await DisconnectAsync(
                        runtime,
                        sessionId,
                        $"shared-memory-under-min: granted={granted}, min={sharedMemoryRequest.MinBytes}",
                        timeout,
                        cancellationToken);

                    return new PluginConnectResult(
                        false,
                        $"Shared memory allocation under MinBytes: granted={granted}, min={sharedMemoryRequest.MinBytes}.",
                        SessionId: sessionId);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            await DisconnectAsync(runtime, sessionId, "connect-exception", TimeSpan.FromSeconds(1), cancellationToken);
            return new PluginConnectResult(false, $"Connect failed: {ex.Message}", SessionId: sessionId);
        }
    }

    public async Task<PluginCommandResult> SendDataAsync(
        string sessionId,
        byte[] data,
        string? transmitTargetId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new PluginCommandResult(false, "Missing sessionId.", ErrorCode: "missing-session");
        }

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            return new PluginCommandResult(false, "Session host not running.", ErrorCode: "session-host-not-running");
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostSendDataPayload(sessionId, data ?? Array.Empty<byte>(), transmitTargetId),
            JsonOptions);

        var response = await sessionHost.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.SendData, SessionId: sessionId, Payload: payload),
            timeout);

        if (response is null)
        {
            return new PluginCommandResult(false, "Session host unavailable.", ErrorCode: "session-host-unavailable");
        }

        if (!response.Ok)
        {
            return new PluginCommandResult(false, response.Error ?? "Send failed.", ErrorCode: "transport-error");
        }

        if (response.Payload is null)
        {
            return new PluginCommandResult(true);
        }

        try
        {
            return response.Payload.Value.Deserialize<PluginCommandResult>(JsonOptions)
                   ?? new PluginCommandResult(true);
        }
        catch
        {
            return new PluginCommandResult(true);
        }
    }

    public async Task<PluginTransmitTargetSnapshot> GetTransmitTargetsAsync(
        string sessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            return new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }

        var payload = JsonSerializer.SerializeToElement(new PluginTransmitTargetQuery(sessionId), JsonOptions);
        var response = await sessionHost.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.GetTransmitTargets, SessionId: sessionId, Payload: payload),
            timeout);

        if (response is not { Ok: true } || response.Payload is null)
        {
            return new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }

        try
        {
            return response.Payload.Value.Deserialize<PluginTransmitTargetSnapshot>(JsonOptions)
                   ?? new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }
        catch
        {
            return new PluginTransmitTargetSnapshot(Array.Empty<PluginTransmitTarget>());
        }
    }

    public async Task<PluginCommandResult> DisconnectAsync(
        PluginRuntime runtime,
        string? sessionId,
        string? reason,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return new PluginCommandResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new PluginCommandResult(false, "Missing sessionId.");
        }

        _logger.LogInformation(
            "Disconnecting session. PluginId={PluginId}, SessionId={SessionId}, Reason={Reason}",
            runtime.Info.Manifest.Id,
            sessionId,
            reason);

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            try
            {
                await _sharedMemorySessionService.CleanupAsync(sessionId);
            }
            catch
            {
            }

            return new PluginCommandResult(true);
        }

        try
        {
            var payload = JsonSerializer.SerializeToElement(new PluginHostDisconnectPayload(sessionId, reason), JsonOptions);
            var response = await sessionHost.Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Disconnect, SessionId: sessionId, Payload: payload),
                timeout);

            if (response is null)
            {
                return new PluginCommandResult(false, "Session host unavailable.");
            }

            if (response.Payload is not null)
            {
                try
                {
                    PluginCommandResult? result = response.Payload.Value.Deserialize<PluginCommandResult>(JsonOptions);
                    if (result is not null)
                    {
                        return result;
                    }
                }
                catch
                {
                }
            }

            return new PluginCommandResult(response.Ok, response.Error);
        }
        finally
        {
            await _sessionHostRuntimeService.StopAsync(sessionId, TimeSpan.FromSeconds(1), reason: reason);
            try
            {
                await _sharedMemorySessionService.CleanupAsync(sessionId);
            }
            catch
            {
            }
        }
    }

    public async Task<SegmentUpgradeResult> AllocateAndApplySharedMemorySegmentAsync(
        PluginRuntime runtime,
        string sessionId,
        int requestedBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return new SegmentUpgradeResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new SegmentUpgradeResult(false, "Missing sessionId.");
        }

        if (requestedBytes <= 0)
        {
            return new SegmentUpgradeResult(false, "requestedBytes must be > 0.");
        }

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            return new SegmentUpgradeResult(false, "Session host not running.");
        }

        SharedMemorySegmentDescriptor descriptor;
        try
        {
            descriptor = await _sharedMemorySessionService.AllocateOrReplaceAsync(sessionId, requestedBytes);
        }
        catch (Exception ex)
        {
            return new SegmentUpgradeResult(false, $"Failed to allocate segment: {ex.Message}");
        }

        var applied = await ApplySharedMemorySegmentAsync(
            runtime,
            sessionId,
            descriptor,
            timeout,
            cancellationToken);

        if (!applied.Ok)
        {
            // Hard-closure: if the plugin host rejects/failed to apply the descriptor,
            // roll back the allocated segment to avoid leaking segments/readers.
            await _sharedMemorySessionService.ReleaseSegmentAsync(sessionId);
            return applied;
        }

        _sharedMemorySessionService.StartReading(sessionId);
        return applied;
    }

    public async Task<SegmentUpgradeResult> ApplySharedMemorySegmentAsync(
        PluginRuntime runtime,
        string sessionId,
        SharedMemorySegmentDescriptor descriptor,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return new SegmentUpgradeResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new SegmentUpgradeResult(false, "Missing sessionId.");
        }

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            return new SegmentUpgradeResult(false, "Session host not running.");
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostApplySharedMemorySegmentPayload(sessionId, descriptor),
            JsonOptions);

        var response = await sessionHost.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.ApplySharedMemorySegment, SessionId: sessionId, Payload: payload),
            timeout);

        if (response is null)
        {
            return new SegmentUpgradeResult(false, "Plugin host unavailable.");
        }

        if (response.Payload is not null)
        {
            try
            {
                var result = response.Payload.Value.Deserialize<SegmentUpgradeResult>(JsonOptions);
                if (result is not null)
                {
                    return result;
                }
            }
            catch
            {
            }
        }

        return new SegmentUpgradeResult(response.Ok, response.Error);
    }

    private static string? ValidateParameters(PluginRuntime runtime, string capabilityId, JsonElement parameters)
    {
        // If capabilities were fetched, enforce capability existence.
        var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
        if (capability is null)
        {
            return $"Unknown capability: {capabilityId}";
        }

        // If a schema is declared, do lite validation.
        if (!string.IsNullOrWhiteSpace(capability.JsonSchema))
        {
            if (!JsonSchemaLiteValidator.TryParseSchema(capability.JsonSchema, out var schema, out var parseError))
            {
                return $"Invalid capability schema: {parseError}";
            }

            if (!JsonSchemaLiteValidator.TryValidate(schema, parameters, out var validateError))
            {
                return $"Parameters validation failed: {validateError}";
            }
        }

        return null;
    }

    private static SessionHostModel ResolveSessionHostModel(PluginCapabilityDescriptor? capability)
    {
        if (capability is null)
        {
            return SessionHostModel.DedicatedPerSession;
        }

        if (capability.SessionHostModel != SessionHostModel.Unspecified)
        {
            return capability.SessionHostModel;
        }

        return capability.SupportsMultiSession
            ? SessionHostModel.SharedPerCapability
            : SessionHostModel.DedicatedPerSession;
    }

    private static string? ComputeMultiSessionGroupId(
        string sessionId,
        SessionHostModel model,
        PluginCapabilityDescriptor? capability,
        JsonElement parameters,
        string? scopeSessionId = null)
    {
        if (model is SessionHostModel.DedicatedPerSession)
        {
            return null;
        }

        if (model is SessionHostModel.SharedPerCapability)
        {
            return null;
        }

        if (model is SessionHostModel.SharedPerScope)
        {
            var keyParam = capability?.SessionHostGroupKeyParameter;
            if (!string.IsNullOrWhiteSpace(keyParam)
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty(keyParam, out var keyProp)
                && keyProp.ValueKind == JsonValueKind.String)
            {
                var key = keyProp.GetString();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }
            }

            return string.IsNullOrWhiteSpace(scopeSessionId) ? sessionId : scopeSessionId;
        }

        return null;
    }
}
