using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

public interface IStorageCalibrationService
{
    StorageCalibrationSnapshot Current { get; }

    event Action<StorageCalibrationSnapshot>? CalibrationChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task<StorageCalibrationSnapshot> RunCalibrationAsync(CancellationToken cancellationToken = default);
}
