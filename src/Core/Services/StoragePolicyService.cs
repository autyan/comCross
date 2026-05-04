using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class StoragePolicyService : IStoragePolicyService, IDisposable
{
    private readonly IStorageHealthService _healthService;
    private readonly object _gate = new();
    private StoragePolicy _current = CreatePolicy(StorageTier.Conservative);

    public StoragePolicyService(IStorageHealthService healthService)
    {
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _current = CreatePolicy(_healthService.Current.Tier);
        _healthService.HealthChanged += OnHealthChanged;
    }

    public StoragePolicy Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event Action<StoragePolicy>? PolicyChanged;

    private void OnHealthChanged(StorageHealthSnapshot snapshot)
    {
        var policy = CreatePolicy(snapshot.Tier);
        lock (_gate)
        {
            if (_current == policy)
            {
                return;
            }

            _current = policy;
        }

        PolicyChanged?.Invoke(policy);
    }

    private static StoragePolicy CreatePolicy(StorageTier tier)
        => tier switch
        {
            StorageTier.Conservative => new StoragePolicy(tier, 4, true, 64),
            StorageTier.Limited => new StoragePolicy(tier, 8, true, 128),
            StorageTier.Normal => new StoragePolicy(tier, 16, false, 256),
            StorageTier.HighCapacity => new StoragePolicy(tier, 32, false, 512),
            _ => new StoragePolicy(StorageTier.Conservative, 4, true, 64)
        };

    public void Dispose()
        => _healthService.HealthChanged -= OnHealthChanged;
}
