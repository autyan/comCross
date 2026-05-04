using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class StorageHealthService : IStorageHealthService
{
    private static readonly TimeSpan NotificationThrottle = TimeSpan.FromMinutes(5);

    private readonly NotificationService _notificationService;
    private readonly ILogger<StorageHealthService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _lastNotificationUtc = new(StringComparer.Ordinal);
    private StorageHealthSnapshot _current = new(StorageHealth.Healthy, StorageTier.Conservative, DateTime.UtcNow);

    public StorageHealthService(NotificationService notificationService, ILogger<StorageHealthService> logger)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StorageHealthSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event Action<StorageHealthSnapshot>? HealthChanged;

    public void ApplyTier(StorageTier tier)
    {
        StorageHealthSnapshot snapshot;
        lock (_gate)
        {
            if (_current.Tier == tier)
            {
                return;
            }

            _current = _current with { Tier = tier, UpdatedAtUtc = DateTime.UtcNow };
            snapshot = _current;
        }

        HealthChanged?.Invoke(snapshot);
    }

    public async Task ReportAsync(
        StorageHealth health,
        string reason,
        NotificationLevel level = NotificationLevel.Warning,
        CancellationToken cancellationToken = default)
    {
        StorageHealthSnapshot snapshot;
        lock (_gate)
        {
            _current = new StorageHealthSnapshot(health, _current.Tier, DateTime.UtcNow, reason);
            snapshot = _current;
        }

        HealthChanged?.Invoke(snapshot);

        if (health is StorageHealth.Healthy or StorageHealth.Busy)
        {
            return;
        }

        var key = $"{health}:{reason}";
        if (!ShouldNotify(key))
        {
            return;
        }

        try
        {
            await _notificationService.AddAsync(
                NotificationCategory.Storage,
                level,
                "notification.storage.healthChanged",
                [health.ToString(), reason],
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to add storage health notification.");
        }
    }

    private bool ShouldNotify(string key)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_lastNotificationUtc.TryGetValue(key, out var last)
                && now - last < NotificationThrottle)
            {
                return false;
            }

            _lastNotificationUtc[key] = now;
            return true;
        }
    }
}
