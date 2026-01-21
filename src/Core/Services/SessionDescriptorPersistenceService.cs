using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Persists committed session definitions (including last successful ParametersJson)
/// into workspace-state.json so sessions can be restored as disconnected on restart.
/// </summary>
public sealed class SessionDescriptorPersistenceService : IDisposable
{
    private readonly ConfigService _configService;
    private readonly DeviceService _deviceService;
    private readonly ILogger<SessionDescriptorPersistenceService> _logger;

    private readonly IDisposable _sessionUpsertSubscription;

    private readonly object _gate = new();
    private CancellationTokenSource? _debounceCts;

    public SessionDescriptorPersistenceService(
        ConfigService configService,
        DeviceService deviceService,
        IEventBus eventBus,
        ILogger<SessionDescriptorPersistenceService> logger)
    {
        _configService = configService;
        _deviceService = deviceService;
        _logger = logger;

        _sessionUpsertSubscription = eventBus.Subscribe<SessionCreatedEvent>(_ => DebouncedSave());
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

        _ = Task.Run(async () =>
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
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var state = await _configService.LoadWorkspaceStateAsync(cancellationToken) ?? new WorkspaceState();
        state.EnsureDefaultWorkload();

        var sessions = _deviceService.GetAllSessions();
        state.SessionDescriptors = sessions
            .Select(s => new SessionDescriptor
            {
                Id = s.Id,
                Name = s.Name,
                AdapterId = s.AdapterId,
                PluginId = s.PluginId,
                CapabilityId = s.CapabilityId,
                ParametersJson = s.ParametersJson,
                EnableDatabaseStorage = s.EnableDatabaseStorage
            })
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        if (string.IsNullOrWhiteSpace(state.Version))
        {
            state.Version = "0.4.0";
        }

        await _configService.SaveWorkspaceStateAsync(state, cancellationToken);
    }

    public void Dispose()
    {
        _sessionUpsertSubscription.Dispose();

        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }
}
