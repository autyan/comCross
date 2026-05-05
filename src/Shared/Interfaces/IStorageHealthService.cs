using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

public interface IStorageHealthService
{
    StorageHealthSnapshot Current { get; }

    event Action<StorageHealthSnapshot>? HealthChanged;

    void ApplyTier(StorageTier tier);

    Task ReportAsync(
        StorageHealth health,
        string reason,
        NotificationLevel level = NotificationLevel.Warning,
        CancellationToken cancellationToken = default);
}
