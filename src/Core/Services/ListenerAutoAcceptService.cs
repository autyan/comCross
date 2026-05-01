using System.Collections.Concurrent;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Orchestrates listener-style sessions that discover inbound peers (e.g. TCP accept, UDP peers)
/// and automatically create child connection sessions.
/// </summary>
public sealed class ListenerAutoAcceptService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly PluginResourceQueryService _resources;
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
        PluginResourceQueryService resources,
        WorkspaceService workspaceService,
        IWorkspaceCoordinator workspaceCoordinator,
        ILogger<ListenerAutoAcceptService> logger)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
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
                    if (s.Kind != SessionKind.Listener
                        || string.IsNullOrWhiteSpace(s.PluginId)
                        || string.IsNullOrWhiteSpace(s.CapabilityId))
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
                        await ProcessListenerPendingAsync(s);
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

    private async Task HandleInvalidationAsync(PluginUiStateInvalidatedCoreEvent invalidated)
    {
        try
        {
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

            if (!string.Equals(session.PluginId, invalidated.PluginId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(session.CapabilityId, invalidated.CapabilityId, StringComparison.Ordinal))
            {
                return;
            }

            if (session.Kind != SessionKind.Listener)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(invalidated.ResourceKind)
                && !string.Equals(invalidated.ResourceKind, PluginResourceKinds.Pending, StringComparison.Ordinal))
            {
                return;
            }

            var gate = _listenerGates.GetOrAdd(listenerSessionId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                await ProcessListenerPendingAsync(session);
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

    private async Task ProcessListenerPendingAsync(Session ownerSession)
    {
        var (ok, error, state) = await _resources.GetResourceListAsync(
            ownerSession,
            PluginResourceKinds.Pending,
            PluginResourceIds.All,
            viewKind: "listener",
            viewInstanceId: null,
            timeout: TimeSpan.FromSeconds(1));

        if (!ok || state is null)
        {
            if (!string.IsNullOrWhiteSpace(error) && !string.Equals(error, "Session host not running.", StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Plugin resource query failed: OwnerSessionId={SessionId}, PluginId={PluginId}, CapabilityId={CapabilityId}, ResourceKind={ResourceKind}, Error={Error}",
                    ownerSession.Id,
                    ownerSession.PluginId,
                    ownerSession.CapabilityId,
                    PluginResourceKinds.Pending,
                    error);
            }

            return;
        }

        if (state.Items.Count == 0)
        {
            return;
        }

        var seen = _seenPendingByListener.GetOrAdd(ownerSession.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        foreach (var item in state.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            var acceptAction = FindConnectScopedAction(item.Actions);
            if (acceptAction is null)
            {
                continue;
            }

            if (!seen.TryAdd(item.Id, 0))
            {
                continue;
            }

            try
            {
                await _workspaceCoordinator.ConnectAsync(
                    ownerSession.PluginId!,
                    ownerSession.CapabilityId!,
                    GetActionParametersJson(acceptAction),
                    GetActionSessionName(acceptAction, item),
                    scopeSessionId: ownerSession.Id,
                    resourceKind: PluginResourceKinds.Pending,
                    resourceId: item.Id);
            }
            catch (Exception ex)
            {
                // Allow retry if connect failed.
                seen.TryRemove(item.Id, out _);
                _logger.LogWarning(
                    ex,
                    "Auto-accept resource connect failed: OwnerSessionId={OwnerSessionId}, ResourceId={ResourceId}, PluginId={PluginId}, CapabilityId={CapabilityId}",
                    ownerSession.Id,
                    item.Id,
                    ownerSession.PluginId,
                    ownerSession.CapabilityId);
            }
        }
    }

    private static PluginResourceActionDescriptor? FindConnectScopedAction(
        IReadOnlyList<PluginResourceActionDescriptor>? actions)
        => actions?.FirstOrDefault(action =>
            string.Equals(action.Id, PluginResourceActionIds.Accept, StringComparison.Ordinal)
            && string.Equals(action.Kind, PluginResourceActionKinds.ConnectScopedResource, StringComparison.Ordinal));

    private static string GetActionParametersJson(PluginResourceActionDescriptor action)
        => action.Parameters is { } parameters ? parameters.GetRawText() : "{}";

    private static string? GetActionSessionName(
        PluginResourceActionDescriptor action,
        PluginManagedResourceItem item)
        => !string.IsNullOrWhiteSpace(action.SessionName)
            ? action.SessionName
            : string.IsNullOrWhiteSpace(item.DisplayName) ? null : item.DisplayName;
}
