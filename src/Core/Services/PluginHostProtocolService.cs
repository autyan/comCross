using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Core-side helpers for standardized PluginHost IPC commands.
///
/// - Strong-typed stable parts (command envelope, known fields)
/// - Plugin-defined parts as JSON + schema-lite validation
/// </summary>
public sealed class PluginHostProtocolService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SharedMemorySessionService _sharedMemorySessionService;
    private readonly SharedMemoryManager _sharedMemoryManager;
    private readonly ILogger<PluginHostProtocolService> _logger;

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public DelegateDisposable(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _dispose();
        }
    }

    public PluginHostProtocolService(
        SharedMemorySessionService sharedMemorySessionService,
        SharedMemoryManager sharedMemoryManager,
        ILogger<PluginHostProtocolService> logger)
    {
        _sharedMemorySessionService = sharedMemorySessionService ?? throw new ArgumentNullException(nameof(sharedMemorySessionService));
        _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(bool Ok, string? Error)> SetBackpressureAsync(
        PluginRuntime runtime,
        string sessionId,
        BackpressureLevel level,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return (false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogDebug(
                "SetBackpressure rejected: invalid sessionId. PluginId={PluginId}",
                runtime.Info.Manifest.Id);
            return (false, "Invalid sessionId.");
        }

        // ADR-010 closure: session-scoped backpressure must target the active exclusive session.
        if (!runtime.IsExclusiveSession(sessionId))
        {
            _logger.LogDebug(
                "SetBackpressure rejected: session not active. PluginId={PluginId}, SessionId={SessionId}",
                runtime.Info.Manifest.Id,
                sessionId);
            return (false, "Session is not active.");
        }

        if (!runtime.IsSessionRegistered(sessionId))
        {
            _logger.LogDebug(
                "SetBackpressure rejected: session not registered. PluginId={PluginId}, SessionId={SessionId}",
                runtime.Info.Manifest.Id,
                sessionId);
            return (false, "Session not registered.");
        }

        _logger.LogDebug(
            "SetBackpressure sending. PluginId={PluginId}, SessionId={SessionId}, Level={Level}",
            runtime.Info.Manifest.Id,
            sessionId,
            level);

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostSetBackpressurePayload(sessionId, level),
            JsonOptions);

        var response = await runtime.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.SetBackpressure, Payload: payload),
            timeout);

        if (response is null)
        {
            return (false, "Plugin host unavailable.");
        }

        return response.Ok ? (true, null) : (false, response.Error);
    }

    public async Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
        PluginRuntime runtime,
        string capabilityId,
        string? sessionId,
        string? viewId,
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
        // Empty/whitespace session ids are invalid and must not bypass gating.
        if (sessionId is not null && string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogDebug(
                "GetUiState rejected: invalid sessionId. PluginId={PluginId}, CapabilityId={CapabilityId}",
                runtime.Info.Manifest.Id,
                capabilityId);
            return (false, "Invalid sessionId.", null);
        }

        // ADR-010: if a sessionId is provided, require per-session binding via event-stream.
        // Sessionless UI (e.g., connect dialog) should pass sessionId: null.
        if (sessionId is not null)
        {
            // ADR-010 (breaking closure): under the single-session model, session-scoped UI must target
            // the currently active (exclusive) session.
            if (!runtime.IsExclusiveSession(sessionId))
            {
                _logger.LogDebug(
                    "GetUiState rejected: session not active. PluginId={PluginId}, SessionId={SessionId}, CapabilityId={CapabilityId}",
                    runtime.Info.Manifest.Id,
                    sessionId,
                    capabilityId);
                return (false, "Session is not active.", null);
            }

            var verifyTimeout = timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1);
            var registered = await runtime.WaitForSessionRegisteredAsync(sessionId, verifyTimeout, cancellationToken);
            if (!registered)
            {
                _logger.LogWarning(
                    "GetUiState rejected: session not registered (handshake timeout). PluginId={PluginId}, SessionId={SessionId}, CapabilityId={CapabilityId}",
                    runtime.Info.Manifest.Id,
                    sessionId,
                    capabilityId);
                return (false, "Session not registered.", null);
            }
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostGetUiStatePayload(capabilityId, sessionId, viewId),
            JsonOptions);

        var response = await runtime.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.GetUiState, Payload: payload),
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

    public async Task<PluginConnectResult> ConnectAsync(
        PluginRuntime runtime,
        string capabilityId,
        JsonElement parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return new PluginConnectResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return new PluginConnectResult(false, "Missing capabilityId.");
        }

        var validationError = ValidateParameters(runtime, capabilityId, parameters);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new PluginConnectResult(false, validationError);
        }

        var sessionId = $"session-{Guid.NewGuid():N}";

        // ADR-010 finalization: current writer injection model is single-session per runtime.
        if (!runtime.TryBeginExclusiveSession(sessionId))
        {
            _logger.LogWarning(
                "Connect rejected: another session already active. PluginId={PluginId}, CapabilityId={CapabilityId}",
                runtime.Info.Manifest.Id,
                capabilityId);
            return new PluginConnectResult(false, "Another session is already active for this plugin runtime.");
        }
        runtime.BeginSessionRegistration(sessionId);

        _logger.LogInformation(
            "Connecting plugin. PluginId={PluginId}, CapabilityId={CapabilityId}, SessionId={SessionId}",
            runtime.Info.Manifest.Id,
            capabilityId,
            sessionId);

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostConnectPayload(capabilityId, parameters, sessionId),
            JsonOptions);
        var response = await runtime.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Connect, Payload: payload),
            timeout);

        if (response is null)
        {
            runtime.EndExclusiveSession(sessionId);
            return new PluginConnectResult(false, "Plugin host unavailable.");
        }

        if (response.Payload is not null)
        {
            try
            {
                PluginConnectResult? result = response.Payload.Value.Deserialize<PluginConnectResult>(JsonOptions);
                if (result is not null)
                {
                    if (!result.Ok)
                    {
                        runtime.EndExclusiveSession(sessionId);
                        return result with { SessionId = sessionId };
                    }

                    if (string.IsNullOrWhiteSpace(result.SessionId))
                    {
                        _logger.LogWarning(
                            "Connect protocol violation: missing SessionId in result. PluginId={PluginId}, SessionId={SessionId}",
                            runtime.Info.Manifest.Id,
                            sessionId);
                        await DisconnectAsync(
                            runtime,
                            sessionId: sessionId,
                            reason: "protocol-violation: connect-result-missing-sessionid",
                            timeout: timeout,
                            cancellationToken: cancellationToken);

                        return new PluginConnectResult(false, "Protocol violation: plugin did not return SessionId.", SessionId: sessionId);
                    }

                    // ADR-010 MVP: verify per-session binding via event-stream.
                    // Treat as required for correctness when sessionId is provided.
                    var verifyTimeoutOnPayload = timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1);
                    var sessionRegisteredOnPayload = await runtime.WaitForSessionRegisteredAsync(sessionId, verifyTimeoutOnPayload, cancellationToken);
                    if (!sessionRegisteredOnPayload)
                    {
                        _logger.LogWarning(
                            "Connect failed: session registration handshake timed out. PluginId={PluginId}, SessionId={SessionId}",
                            runtime.Info.Manifest.Id,
                            sessionId);
                        await DisconnectAsync(
                            runtime,
                            sessionId: sessionId,
                            reason: "session-registration-timeout",
                            timeout: timeout,
                            cancellationToken: cancellationToken);

                        return new PluginConnectResult(false, "Plugin host session registration handshake timed out.", SessionId: sessionId);
                    }

                    // ADR-010 closure: single-session-per-runtime => plugin must echo SessionId (if provided).
                    if (!string.IsNullOrWhiteSpace(result.SessionId)
                        && !string.Equals(result.SessionId, sessionId, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "Connect protocol violation: SessionId mismatch. PluginId={PluginId}, Expected={Expected}, Actual={Actual}",
                            runtime.Info.Manifest.Id,
                            sessionId,
                            result.SessionId);
                        await DisconnectAsync(
                            runtime,
                            sessionId: sessionId,
                            reason: "protocol-violation: connect-result-sessionid-mismatch",
                            timeout: timeout,
                            cancellationToken: cancellationToken);

                        return new PluginConnectResult(false, "Protocol violation: plugin returned mismatched SessionId.", SessionId: sessionId);
                    }

                    // ADR-010 closure: if shared memory is declared by the capability, initialize it as part of connect.
                    var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
                    var sharedMemoryRequest = capability?.SharedMemoryRequest;
                    if (sharedMemoryRequest is not null)
                    {
                        _logger.LogInformation(
                            "Connect requires shared memory. PluginId={PluginId}, SessionId={SessionId}, MinBytes={MinBytes}, PreferredBytes={PreferredBytes}",
                            runtime.Info.Manifest.Id,
                            sessionId,
                            sharedMemoryRequest.MinBytes,
                            sharedMemoryRequest.PreferredBytes);
                        var requestedBytes = sharedMemoryRequest.PreferredBytes > 0
                            ? sharedMemoryRequest.PreferredBytes
                            : sharedMemoryRequest.MinBytes;

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

                        // Enforce MinBytes contract after allocation (host may reject if cannot allocate at least this size).
                        if (sharedMemoryRequest.MinBytes > 0 && upgraded.GrantedBytes is int granted && granted < sharedMemoryRequest.MinBytes)
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

                        // ADR-010 closure: once shared memory is active for a session, forward backpressure
                        // signals to PluginHost (session-scoped, gated by exclusive+registered session).
                        void Handler(string bpSessionId, BackpressureLevel level)
                        {
                            if (!string.Equals(bpSessionId, sessionId, StringComparison.Ordinal))
                            {
                                return;
                            }

                            _logger.LogDebug(
                                "Backpressure detected. PluginId={PluginId}, SessionId={SessionId}, Level={Level}",
                                runtime.Info.Manifest.Id,
                                sessionId,
                                level);

                            var backpressureTimeout = TimeSpan.FromMilliseconds(200);

                            // Fire-and-forget: backpressure is best-effort.
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await SetBackpressureAsync(runtime, sessionId, level, backpressureTimeout, CancellationToken.None);
                                }
                                catch
                                {
                                }
                            }, CancellationToken.None);
                        }

                        _sharedMemoryManager.BackpressureDetected += Handler;
                        runtime.RegisterSessionDisposable(sessionId, new DelegateDisposable(() => _sharedMemoryManager.BackpressureDetected -= Handler));
                    }

                    return result with { SessionId = sessionId };
                }
            }
            catch
            {
                // Fall back to envelope values.
            }
        }

        if (!response.Ok)
        {
            runtime.EndExclusiveSession(sessionId);
            return new PluginConnectResult(false, response.Error);
        }

        // ADR-010 MVP: verify per-session binding via event-stream.
        // This is best-effort but treated as required for correctness when sessionId is provided.
        var verifyTimeout = timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1);
        var registered = await runtime.WaitForSessionRegisteredAsync(sessionId, verifyTimeout, cancellationToken);
        if (!registered)
        {
            _logger.LogWarning(
                "Connect failed: session registration handshake timed out. PluginId={PluginId}, SessionId={SessionId}",
                runtime.Info.Manifest.Id,
                sessionId);
            await DisconnectAsync(
                runtime,
                sessionId: sessionId,
                reason: "session-registration-timeout",
                timeout: timeout,
                cancellationToken: cancellationToken);

            return new PluginConnectResult(false, "Plugin host session registration handshake timed out.", SessionId: sessionId);
        }

        // ADR-010 closure: if shared memory is declared by the capability, initialize it as part of connect.
        var connectedCapability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
        var connectedSharedMemoryRequest = connectedCapability?.SharedMemoryRequest;
        if (connectedSharedMemoryRequest is not null)
        {
            _logger.LogInformation(
                "Connect requires shared memory. PluginId={PluginId}, SessionId={SessionId}, MinBytes={MinBytes}, PreferredBytes={PreferredBytes}",
                runtime.Info.Manifest.Id,
                sessionId,
                connectedSharedMemoryRequest.MinBytes,
                connectedSharedMemoryRequest.PreferredBytes);
            var requestedBytes = connectedSharedMemoryRequest.PreferredBytes > 0
                ? connectedSharedMemoryRequest.PreferredBytes
                : connectedSharedMemoryRequest.MinBytes;

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

            if (connectedSharedMemoryRequest.MinBytes > 0 && upgraded.GrantedBytes is int granted && granted < connectedSharedMemoryRequest.MinBytes)
            {
                _logger.LogWarning(
                    "Shared memory under MinBytes on connect. PluginId={PluginId}, SessionId={SessionId}, GrantedBytes={GrantedBytes}, MinBytes={MinBytes}",
                    runtime.Info.Manifest.Id,
                    sessionId,
                    granted,
                    connectedSharedMemoryRequest.MinBytes);
                await DisconnectAsync(
                    runtime,
                    sessionId: sessionId,
                    reason: $"shared-memory-under-min: granted={granted}, min={connectedSharedMemoryRequest.MinBytes}",
                    timeout: timeout,
                    cancellationToken: cancellationToken);

                return new PluginConnectResult(
                    false,
                    $"Shared memory allocation under MinBytes: granted={granted}, min={connectedSharedMemoryRequest.MinBytes}.",
                    SessionId: sessionId);
            }
        }

        return new PluginConnectResult(true, SessionId: sessionId);
    }

    public async Task<PluginCommandResult> DisconnectAsync(
        PluginRuntime runtime,
        string? sessionId,
        string? reason,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
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

        // ADR-010 closure: disconnect is session-scoped under the single-session runtime model.
        if (!string.IsNullOrWhiteSpace(sessionId) && !runtime.IsExclusiveSession(sessionId))
        {
            return new PluginCommandResult(false, "Session is not active.");
        }

        async Task CleanupAsync()
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            try
            {
                await _sharedMemorySessionService.CleanupAsync(sessionId);
            }
            catch
            {
            }

            try
            {
                runtime.EndExclusiveSession(sessionId);
            }
            catch
            {
            }
        }

        var payload = JsonSerializer.SerializeToElement(new PluginHostDisconnectPayload(sessionId, reason), JsonOptions);
        var response = await runtime.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Disconnect, Payload: payload),
            timeout);

        if (response is null)
        {
            await CleanupAsync();
            return new PluginCommandResult(false, "Plugin host unavailable.");
        }

        if (response.Payload is not null)
        {
            try
            {
                PluginCommandResult? result = response.Payload.Value.Deserialize<PluginCommandResult>(JsonOptions);
                if (result is not null)
                {
                    await CleanupAsync();
                    return result;
                }
            }
            catch
            {
                // Fall back to envelope values.
            }
        }

        await CleanupAsync();
        return new PluginCommandResult(response.Ok, response.Error);
    }

    public async Task<SegmentUpgradeResult> AllocateAndApplySharedMemorySegmentAsync(
        PluginRuntime runtime,
        string sessionId,
        int requestedBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return new SegmentUpgradeResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new SegmentUpgradeResult(false, "Missing sessionId.");
        }

        if (!runtime.IsExclusiveSession(sessionId))
        {
            return new SegmentUpgradeResult(false, "Session is not active.");
        }

        if (requestedBytes <= 0)
        {
            return new SegmentUpgradeResult(false, "requestedBytes must be > 0.");
        }

        // Gate by per-session registration (ADR-010 MVP correctness).
        var verifyTimeout = timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1);
        var registered = await runtime.WaitForSessionRegisteredAsync(sessionId, verifyTimeout, cancellationToken);
        if (!registered)
        {
            return new SegmentUpgradeResult(false, "Session not registered.");
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
        if (runtime.State != PluginLoadState.Loaded || runtime.Client is null)
        {
            return new SegmentUpgradeResult(false, "Plugin not loaded.");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new SegmentUpgradeResult(false, "Missing sessionId.");
        }

        if (!runtime.IsExclusiveSession(sessionId))
        {
            return new SegmentUpgradeResult(false, "Session is not active.");
        }

        // Gate by per-session registration (ADR-010 MVP correctness).
        var verifyTimeout = timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1);
        var registered = await runtime.WaitForSessionRegisteredAsync(sessionId, verifyTimeout, cancellationToken);
        if (!registered)
        {
            return new SegmentUpgradeResult(false, "Session not registered.");
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostApplySharedMemorySegmentPayload(sessionId, descriptor),
            JsonOptions);

        var response = await runtime.Client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.ApplySharedMemorySegment, Payload: payload),
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
}
