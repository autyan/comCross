using System.Collections.Concurrent;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Orchestrates sessions that expose plugin-managed pending resources and automatically create child sessions.
/// </summary>
public sealed class ManagedResourceAutoConnectService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly PluginResourceQueryService _resources;
    private readonly WorkspaceService _workspaceService;
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly ILogger<ManagedResourceAutoConnectService> _logger;

    private readonly CancellationTokenSource _pollCts = new();
    private readonly PeriodicTimer _pollTimer = new(PollInterval);

    // ownerSessionId -> (resourceId -> seen)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _seenPendingByOwner = new(StringComparer.Ordinal);

    // ownerSessionId -> gate
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _ownerGates = new(StringComparer.Ordinal);

    public ManagedResourceAutoConnectService(
        IEventBus eventBus,
        PluginResourceQueryService resources,
        WorkspaceService workspaceService,
        IWorkspaceCoordinator workspaceCoordinator,
        ILogger<ManagedResourceAutoConnectService> logger)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _workspaceCoordinator = workspaceCoordinator ?? throw new ArgumentNullException(nameof(workspaceCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        eventBus.Subscribe<PluginUiStateInvalidatedCoreEvent>(evt => _ = HandleInvalidationAsync(evt));

        // Robustness: in case UI-state invalidations are missed, poll active managed-resource sessions.
        _ = Task.Run(PollManagedResourceSessionsAsync);
    }

    private async Task PollManagedResourceSessionsAsync()
    {
        try
        {
            while (await _pollTimer.WaitForNextTickAsync(_pollCts.Token))
            {
                var sessions = _workspaceService.GetAllSessions();
                foreach (var s in sessions)
                {
                    if (string.IsNullOrWhiteSpace(s.PluginId)
                        || string.IsNullOrWhiteSpace(s.CapabilityId))
                    {
                        continue;
                    }

                    if (!s.HasManagedResourceKind(PluginResourceKinds.Pending))
                    {
                        continue;
                    }

                    var gate = _ownerGates.GetOrAdd(s.Id, _ => new SemaphoreSlim(1, 1));
                    if (!await gate.WaitAsync(0))
                    {
                        continue;
                    }

                    try
                    {
                        await ProcessPendingResourcesAsync(s);
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

            // Only react to resource-scoped invalidations.
            var ownerSessionId = invalidated.SessionId;

            var session = _workspaceService.GetSession(ownerSessionId);
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

            if (!session.HasManagedResourceKind(PluginResourceKinds.Pending))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(invalidated.ResourceKind)
                && !string.Equals(invalidated.ResourceKind, PluginResourceKinds.Pending, StringComparison.Ordinal))
            {
                return;
            }

            var gate = _ownerGates.GetOrAdd(ownerSessionId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                await ProcessPendingResourcesAsync(session);
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

    private async Task ProcessPendingResourcesAsync(Session ownerSession)
    {
        var (ok, error, state) = await _resources.GetResourceListAsync(
            ownerSession,
            PluginResourceKinds.Pending,
            PluginResourceIds.All,
            viewKind: "managed-resource",
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

        var seen = _seenPendingByOwner.GetOrAdd(ownerSession.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
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
