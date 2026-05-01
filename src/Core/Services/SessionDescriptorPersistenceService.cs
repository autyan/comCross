using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Persists committed session definitions (including last successful ParametersJson)
/// into workspace-state.json so sessions can be restored as disconnected on restart.
/// </summary>
public sealed class SessionDescriptorPersistenceService : IDisposable, IAsyncDisposable
{
    private readonly WorkspaceStateStore _workspaceStateStore;
    private readonly DeviceService _deviceService;
    private readonly ILogger<SessionDescriptorPersistenceService> _logger;

    private readonly IDisposable _sessionUpsertSubscription;
    private readonly IDisposable _sessionRenamedSubscription;

    private readonly object _gate = new();
    private CancellationTokenSource? _debounceCts;
    private Task? _pendingSaveTask;

    public SessionDescriptorPersistenceService(
        WorkspaceStateStore workspaceStateStore,
        DeviceService deviceService,
        IEventBus eventBus,
        ILogger<SessionDescriptorPersistenceService> logger)
    {
        _workspaceStateStore = workspaceStateStore;
        _deviceService = deviceService;
        _logger = logger;

        _sessionUpsertSubscription = eventBus.Subscribe<SessionCreatedEvent>(_ => DebouncedSave());
        _sessionRenamedSubscription = eventBus.Subscribe<SessionRenamedEvent>(_ => DebouncedSave());
    }

    private void DebouncedSave()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }

        var saveTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await SaveAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to persist session descriptors");
            }
        });

        lock (_gate)
        {
            _pendingSaveTask = saveTask;
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task? pending;
        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            pending = _pendingSaveTask;
        }

        if (pending is not null)
        {
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch
            {
                // A canceled debounce is expected; the explicit save below is authoritative.
            }
        }

        await SaveAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var sessions = _deviceService.GetAllSessions();
        var descriptors = sessions
            .Select(static s => new SessionDescriptor
            {
                Id = s.Id,
                Name = s.Name,
                AdapterId = s.AdapterId,
                PluginId = s.PluginId,
                CapabilityId = s.CapabilityId,
                ParametersJson = s.ParametersJson,
                DisplayTitle = s.DisplayTitle,
                DisplaySubtitle = s.DisplaySubtitle,
                DisplayIcon = s.DisplayIcon,
                EnableDatabaseStorage = s.EnableDatabaseStorage,
                ParentSessionId = s.ParentSessionId,
                ManagedResourceKinds = s.ManagedResourceKinds.ToList()
            })
            .ToList();

        await _workspaceStateStore.UpdateAsync(state =>
        {
            var runtimeIds = descriptors.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            var merged = new List<SessionDescriptor>(descriptors.Count + state.SessionDescriptors.Count);
            merged.AddRange(descriptors);

            foreach (var existing in state.SessionDescriptors)
            {
                if (!string.IsNullOrWhiteSpace(existing.Id) && !runtimeIds.Contains(existing.Id))
                {
                    merged.Add(existing);
                }
            }

            state.SessionDescriptors = merged;
        }, cancellationToken);
    }

    public void Dispose()
    {
        _sessionUpsertSubscription.Dispose();
        _sessionRenamedSubscription.Dispose();

        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            _pendingSaveTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        Dispose();
    }
}
