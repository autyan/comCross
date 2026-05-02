using ComCross.Core.Models;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class PluginSessionInitializationService
{
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(5);

    private readonly DeviceService _deviceService;
    private readonly PluginManagerService _pluginManager;
    private readonly PluginHostProtocolService _protocolService;
    private readonly PluginSessionStorageService _storageService;
    private readonly WorkspaceStateStore _workspaceStateStore;
    private readonly ILogger<PluginSessionInitializationService> _logger;

    public PluginSessionInitializationService(
        DeviceService deviceService,
        PluginManagerService pluginManager,
        PluginHostProtocolService protocolService,
        PluginSessionStorageService storageService,
        WorkspaceStateStore workspaceStateStore,
        ILogger<PluginSessionInitializationService> logger)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _protocolService = protocolService ?? throw new ArgumentNullException(nameof(protocolService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _workspaceStateStore = workspaceStateStore ?? throw new ArgumentNullException(nameof(workspaceStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeRestoredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = _deviceService.GetAllSessions()
            .Where(session => session.InitializationState is not SessionInitializationState.Updating)
            .ToList();

        foreach (var session in sessions)
        {
            await InitializeSessionAsync(session, cancellationToken);
        }
    }

    private async Task InitializeSessionAsync(Session session, CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(session.Id, cancellationToken);

        if (string.IsNullOrWhiteSpace(session.PluginId) || string.IsNullOrWhiteSpace(session.CapabilityId))
        {
            MarkReady(session);
            return;
        }

        var runtime = _pluginManager.GetRuntime(session.PluginId);
        if (runtime is null || runtime.State != PluginLoadState.Loaded)
        {
            MarkUnavailable(session, "Plugin unavailable.");
            return;
        }

        session.InitializationState = SessionInitializationState.Updating;
        session.InitializationError = null;
        _deviceService.UpdateSession(session);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(InitializationTimeout);

        try
        {
            var storage = await _storageService.LoadAsync(session.PluginId, session.Id, timeoutCts.Token);
            var context = new PluginSessionStateInitializationContext(
                session.PluginId,
                session.CapabilityId!,
                session.Id,
                runtime.Info.Manifest.Version,
                descriptor?.LastInitializedPluginVersion,
                session.ParametersJson,
                storage);

            var (ok, error, result) = await _protocolService.InitializeSessionStateAsync(
                runtime,
                context,
                InitializationTimeout,
                timeoutCts.Token);

            if (!ok || result is null || !result.Ok)
            {
                MarkFailed(session, result?.Error ?? error ?? "Session initialization failed.");
                return;
            }

            await _storageService.ApplyPatchAsync(session.PluginId, session.Id, result.StoragePatch, timeoutCts.Token);
            ApplyPatch(session, result.SessionPatch);
            session.InitializationState = SessionInitializationState.Ready;
            session.InitializationError = null;
            _deviceService.UpdateSession(session);
            await PersistInitializationMetadataAsync(session, runtime.Info.Manifest.Version, result.StoragePatch?.SchemaVersion, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            MarkFailed(session, "Session initialization timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session initialization failed: SessionId={SessionId}", session.Id);
            MarkFailed(session, ex.Message);
        }
    }

    private async Task<SessionDescriptor?> FindDescriptorAsync(string sessionId, CancellationToken cancellationToken)
    {
        var state = await _workspaceStateStore.LoadAsync(cancellationToken);
        return state.SessionDescriptors.FirstOrDefault(d => string.Equals(d.Id, sessionId, StringComparison.Ordinal));
    }

    private void MarkReady(Session session)
    {
        session.InitializationState = SessionInitializationState.Ready;
        session.InitializationError = null;
        _deviceService.UpdateSession(session);
    }

    private void MarkUnavailable(Session session, string error)
    {
        session.InitializationState = SessionInitializationState.PluginUnavailable;
        session.InitializationError = error;
        _deviceService.UpdateSession(session);
    }

    private void MarkFailed(Session session, string error)
    {
        session.InitializationState = SessionInitializationState.Failed;
        session.InitializationError = error;
        _deviceService.UpdateSession(session);
    }

    private static void ApplyPatch(Session session, PluginSessionMetadataPatch? patch)
    {
        if (patch is null)
        {
            return;
        }

        if (patch.ParametersJson is not null)
        {
            session.ParametersJson = patch.ParametersJson;
        }

        if (patch.DisplayTitle is not null)
        {
            session.DisplayTitle = patch.DisplayTitle;
        }

        if (patch.DisplaySubtitle is not null)
        {
            session.DisplaySubtitle = patch.DisplaySubtitle;
        }

        if (patch.DisplayIcon is not null)
        {
            session.DisplayIcon = patch.DisplayIcon;
        }

        if (patch.CanReconnect is not null)
        {
            session.CanReconnect = patch.CanReconnect.Value;
        }

        if (patch.ParentSessionId is not null)
        {
            session.ParentSessionId = patch.ParentSessionId;
        }

        if (patch.ManagedResourceKinds is not null)
        {
            session.ManagedResourceKinds = patch.ManagedResourceKinds;
        }
    }

    private async Task PersistInitializationMetadataAsync(
        Session session,
        string? pluginVersion,
        int? storageSchemaVersion,
        CancellationToken cancellationToken)
    {
        await _workspaceStateStore.UpdateAsync(state =>
        {
            var descriptor = state.SessionDescriptors.FirstOrDefault(d => string.Equals(d.Id, session.Id, StringComparison.Ordinal));
            if (descriptor is null)
            {
                return;
            }

            descriptor.ParametersJson = session.ParametersJson;
            descriptor.DisplayTitle = session.DisplayTitle;
            descriptor.DisplaySubtitle = session.DisplaySubtitle;
            descriptor.DisplayIcon = session.DisplayIcon;
            descriptor.CanReconnect = session.CanReconnect;
            descriptor.InitializationState = session.InitializationState;
            descriptor.InitializationError = session.InitializationError;
            descriptor.LastInitializedPluginVersion = pluginVersion;
            if (storageSchemaVersion is { } version)
            {
                descriptor.StorageSchemaVersion = version;
            }
            descriptor.ParentSessionId = session.ParentSessionId;
            descriptor.ManagedResourceKinds = session.ManagedResourceKinds.ToList();
        }, cancellationToken);
    }
}
