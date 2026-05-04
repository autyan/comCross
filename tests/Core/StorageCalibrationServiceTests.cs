using ComCross.Core.Services;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class StorageCalibrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "comcross-calibration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunCalibrationAsync_CompletesAndPersistsTier()
    {
        var (service, health, _) = await CreateCalibrationServiceAsync();

        var result = await service.RunCalibrationAsync();
        var reloaded = await CreateCalibrationServiceAsync();
        await reloaded.Service.StartAsync();

        Assert.Equal(StorageCalibrationPhase.Completed, result.Phase);
        Assert.NotNull(result.FingerprintHash);
        Assert.NotNull(result.LastCalibratedAtUtc);
        Assert.Equal(result.Tier, health.Current.Tier);
        Assert.Equal(StorageCalibrationPhase.Completed, reloaded.Service.Current.Phase);
        Assert.Equal(result.Tier, reloaded.Service.Current.Tier);
    }

    [Fact]
    public async Task StorageHealthService_ReportAsync_UpdatesStateAndAddsNotification()
    {
        var paths = CreatePaths();
        var settings = new SettingsService(new ConfigService(paths), new AppDatabase(paths), paths);
        await settings.InitializeAsync();
        var database = new AppDatabase(paths);
        await database.InitializeAsync();
        var notification = new NotificationService(database, settings);
        var health = new StorageHealthService(notification, NullLogger<StorageHealthService>.Instance);

        await health.ReportAsync(StorageHealth.Degraded, "test-pressure");

        var items = await notification.GetRecentAsync();
        var item = Assert.Single(items);
        Assert.Equal(StorageHealth.Degraded, health.Current.Health);
        Assert.Equal(NotificationCategory.Storage, item.Category);
        Assert.Equal("notification.storage.healthChanged", item.MessageKey);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private async Task<(StorageCalibrationService Service, StorageHealthService Health, NotificationService Notification)> CreateCalibrationServiceAsync()
    {
        var paths = CreatePaths();
        var settings = new SettingsService(new ConfigService(paths), new AppDatabase(paths), paths);
        await settings.InitializeAsync();
        var database = new AppDatabase(paths);
        await database.InitializeAsync();
        var notification = new NotificationService(database, settings);
        var health = new StorageHealthService(notification, NullLogger<StorageHealthService>.Instance);
        var service = new StorageCalibrationService(paths, notification, health, NullLogger<StorageCalibrationService>.Instance);
        return (service, health, notification);
    }

    private ComCrossPathService CreatePaths()
        => new(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "config"),
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"));
}
