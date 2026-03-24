using System.Collections.Concurrent;
using System.Text.Json;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Orchestrates listener-style sessions that discover inbound peers (e.g. TCP accept, UDP peers)
/// and automatically create child connection sessions.
///
/// Current MVP implementation targets the official network adapter plugin.
/// Listener and child sessions share a session-host process grouped by listener session id.
/// </summary>
public sealed class ListenerAutoAcceptService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly PluginManagerService _pluginManagerService;
    private readonly PluginHostProtocolService _protocol;
    private readonly WorkspaceService _workspaceService;
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly ILogger<ListenerAutoAcceptService> _logger;

    private readonly CancellationTokenSource _pollCts = new();
    private readonly PeriodicTimer _pollTimer = new(PollInterval);

    // listenerSessionId -> (pendingId -> seen)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _seenPendingByListener = new(StringComparer.Ordinal);

    // listenerSessionId -> gate
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _listenerGates = new(StringComparer.Ordinal);

    public ListenerAutoAcceptService(
        IEventBus eventBus,
        PluginManagerService pluginManagerService,
        PluginHostProtocolService protocol,
        WorkspaceService workspaceService,
        IWorkspaceCoordinator workspaceCoordinator,
        ILogger<ListenerAutoAcceptService> logger)
    {
        _pluginManagerService = pluginManagerService ?? throw new ArgumentNullException(nameof(pluginManagerService));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _workspaceCoordinator = workspaceCoordinator ?? throw new ArgumentNullException(nameof(workspaceCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        eventBus.Subscribe<PluginUiStateInvalidatedCoreEvent>(evt => _ = HandleInvalidationAsync(evt));

        // Robustness: in case UI-state invalidations are missed, poll active listener sessions.
        _ = Task.Run(PollListenersAsync);
    }

    private async Task PollListenersAsync()
    {
        try
        {
            while (await _pollTimer.WaitForNextTickAsync(_pollCts.Token))
            {
                var sessions = _workspaceService.GetAllSessions();
                foreach (var s in sessions)
                {
                    if (!string.Equals(s.PluginId, "network.adapter", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (s.CapabilityId is not ("tcp.server" or "udp.listen"))
                    {
                        continue;
                    }

                    // Skip bind-connection sessions.
                    if (IsBindMode(s.ParametersJson))
                    {
                        continue;
                    }

                    var gate = _listenerGates.GetOrAdd(s.Id, _ => new SemaphoreSlim(1, 1));
                    if (!await gate.WaitAsync(0))
                    {
                        continue;
                    }

                    try
                    {
                        await ProcessListenerPendingAsync(s.Id, s.CapabilityId!);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // best-effort
        }
    }

    private static bool IsBindMode(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("mode", out var modeEl))
            {
                return false;
            }

            var mode = modeEl.ValueKind == JsonValueKind.String ? modeEl.GetString() : modeEl.ToString();
            return string.Equals(mode, "bind", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleInvalidationAsync(PluginUiStateInvalidatedCoreEvent invalidated)
    {
        try
        {
            if (!string.Equals(invalidated.PluginId, "network.adapter", StringComparison.Ordinal))
            {
                return;
            }

            if (invalidated is null || string.IsNullOrWhiteSpace(invalidated.CapabilityId) || invalidated.SessionId is null)
            {
                return;
            }

            // Only react to listener-scoped invalidations.
            var listenerSessionId = invalidated.SessionId;

            var session = _workspaceService.GetSession(listenerSessionId);
            if (session is null)
            {
                return;
            }

            // MVP: only the official network adapter defines listener sessions today.
            if (!string.Equals(session.PluginId, "network.adapter", StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(session.CapabilityId, invalidated.CapabilityId, StringComparison.Ordinal))
            {
                return;
            }

            if (invalidated.CapabilityId is not ("tcp.server" or "udp.listen"))
            {
                return;
            }

            var gate = _listenerGates.GetOrAdd(listenerSessionId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                await ProcessListenerPendingAsync(listenerSessionId, invalidated.CapabilityId);
            }
            finally
            {
                gate.Release();
            }
        }
        catch
        {
            // Never throw from a fire-and-forget handler.
        }
    }

    private async Task ProcessListenerPendingAsync(string listenerSessionId, string capabilityId)
    {
        var runtime = _pluginManagerService.GetRuntime("network.adapter");
        if (runtime is null)
        {
            return;
        }

        var (ok, error, snapshot) = await _protocol.GetUiStateAsync(
            runtime,
            capabilityId,
            sessionId: listenerSessionId,
            viewKind: null,
            viewInstanceId: null,
            timeout: TimeSpan.FromSeconds(1));

        if (!ok || snapshot is null)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogDebug(
                    "Listener UI state fetch failed: ListenerSessionId={SessionId}, CapabilityId={CapabilityId}, Error={Error}",
                    listenerSessionId,
                    capabilityId,
                    error);
            }

            return;
        }

        if (snapshot.State.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryReadPendingConnections(snapshot.State, out var pending))
        {
            return;
        }

        if (pending.Count == 0)
        {
            return;
        }

        var seen = _seenPendingByListener.GetOrAdd(listenerSessionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));


        foreach (var p in pending)
        {
            if (string.IsNullOrWhiteSpace(p.PendingId))
            {
                continue;
            }

            if (!seen.TryAdd(p.PendingId, 0))
            {
                continue;
            }

            object parameters = new
            {
                mode = "bind",
                listenerSessionId,
                pendingId = p.PendingId,
                endpoint = p.DisplayName
            };

            if (TryParseHostPort(p.DisplayName, out var host, out var port))
            {
                parameters = capabilityId switch
                {
                    // Extra fields are for UI/display only; bind uses listenerSessionId/pendingId.
                    "tcp.server" => new { mode = "bind", listenerSessionId, pendingId = p.PendingId, host, port, endpoint = p.DisplayName },
                    "udp.listen" => new { mode = "bind", listenerSessionId, pendingId = p.PendingId, remoteHost = host, remotePort = port, endpoint = p.DisplayName },
                    _ => parameters
                };
            }

            var parametersJson = JsonSerializer.Serialize(parameters);

            var sessionName = string.IsNullOrWhiteSpace(p.DisplayName)
                ? null
                : p.DisplayName;

            try
            {
                await _workspaceCoordinator.ConnectAsync("network.adapter", capabilityId, parametersJson, sessionName);
            }
            catch (Exception ex)
            {
                // Allow retry if connect failed.
                seen.TryRemove(p.PendingId, out _);
                _logger.LogWarning(
                    ex,
                    "Auto-accept connect failed: ListenerSessionId={ListenerSessionId}, PendingId={PendingId}, CapabilityId={CapabilityId}",
                    listenerSessionId,
                    p.PendingId,
                    capabilityId);
            }
        }
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

    private static bool TryReadPendingConnections(JsonElement state, out List<PendingConnection> pending)
    {
        pending = new List<PendingConnection>();

        // Preferred: pendingConnections
        if (!state.TryGetProperty("pendingConnections", out var arr) && !state.TryGetProperty("pending", out arr))
        {
            return false;
        }

        if (arr.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;

            var display = item.TryGetProperty("displayName", out var dnEl) && dnEl.ValueKind == JsonValueKind.String
                ? dnEl.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(id))
            {
                pending.Add(new PendingConnection(id!, display));
            }
        }

        return true;
    }

    private readonly record struct PendingConnection(string PendingId, string? DisplayName);
}
