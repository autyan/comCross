using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class StorageCalibrationService : IStorageCalibrationService
{
    private const int FingerprintSchemaVersion = 1;
    private const int CalibrationSchemaVersion = 1;
    private const int SampleBytes = 4 * 1024 * 1024;

    private readonly ComCrossPathService _paths;
    private readonly NotificationService _notificationService;
    private readonly IStorageHealthService _healthService;
    private readonly ILogger<StorageCalibrationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly object _stateGate = new();
    private StorageCalibrationSnapshot _current;

    public StorageCalibrationService(
        ComCrossPathService paths,
        NotificationService notificationService,
        IStorageHealthService healthService,
        ILogger<StorageCalibrationService> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _current = Conservative("startup");
    }

    public StorageCalibrationSnapshot Current
    {
        get
        {
            lock (_stateGate)
            {
                return _current;
            }
        }
    }

    public event Action<StorageCalibrationSnapshot>? CalibrationChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var fingerprint = ComputeFingerprintHash();
        var stored = await TryLoadStoredAsync(cancellationToken);
        if (stored is not null && string.Equals(stored.FingerprintHash, fingerprint, StringComparison.Ordinal))
        {
            var snapshot = new StorageCalibrationSnapshot(
                StorageCalibrationPhase.Completed,
                stored.Tier,
                stored.FingerprintHash,
                stored.CalibratedAtUtc,
                _paths.SessionSpoolDirectory,
                "loaded");
            SetCurrent(snapshot);
            _healthService.ApplyTier(snapshot.Tier);
            return;
        }

        SetCurrent(Conservative(stored is null ? "not-calibrated" : "fingerprint-changed", fingerprint));
        if (stored is not null)
        {
            await NotifyAsync(
                NotificationLevel.Warning,
                "notification.storage.calibrationReset",
                cancellationToken,
                "fingerprint-changed");
        }

        _ = Task.Run(() => RunCalibrationAsync(CancellationToken.None), CancellationToken.None);
    }

    public async Task<StorageCalibrationSnapshot> RunCalibrationAsync(CancellationToken cancellationToken = default)
    {
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = ComputeFingerprintHash();
            SetCurrent(new StorageCalibrationSnapshot(
                StorageCalibrationPhase.Calibrating,
                StorageTier.Conservative,
                fingerprint,
                null,
                _paths.SessionSpoolDirectory,
                "running"));

            var tier = await MeasureTierAsync(cancellationToken);
            var snapshot = new StorageCalibrationSnapshot(
                StorageCalibrationPhase.Completed,
                tier,
                fingerprint,
                DateTime.UtcNow,
                _paths.SessionSpoolDirectory,
                "completed");

            await SaveStoredAsync(snapshot, cancellationToken);
            SetCurrent(snapshot);
            _healthService.ApplyTier(tier);
            await NotifyAsync(NotificationLevel.Info, "notification.storage.calibrationCompleted", cancellationToken, tier.ToString());
            return snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Storage calibration failed.");
            var snapshot = Conservative("failed", ComputeFingerprintHash()) with
            {
                Phase = StorageCalibrationPhase.Failed,
                LastCalibratedAtUtc = null
            };
            SetCurrent(snapshot);
            _healthService.ApplyTier(StorageTier.Conservative);
            await NotifyAsync(NotificationLevel.Warning, "notification.storage.calibrationFailed", CancellationToken.None);
            return snapshot;
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<StorageTier> MeasureTierAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_paths.SessionSpoolDirectory, "_calibration");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"cal-{Guid.NewGuid():N}.tmp");
        var buffer = new byte[256 * 1024];
        Random.Shared.NextBytes(buffer);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough))
            {
                var remaining = SampleBytes;
                while (remaining > 0)
                {
                    var take = Math.Min(buffer.Length, remaining);
                    await stream.WriteAsync(buffer.AsMemory(0, take), cancellationToken);
                    remaining -= take;
                }

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
            {
                while (await stream.ReadAsync(buffer, cancellationToken) > 0)
                {
                }
            }

            stopwatch.Stop();
            var mbPerSecond = (SampleBytes / 1024d / 1024d) / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
            return Classify(mbPerSecond);
        }
        finally
        {
            TryDelete(path);
            TryDeleteDirectory(directory);
        }
    }

    private static StorageTier Classify(double mbPerSecond)
        => mbPerSecond switch
        {
            < 8 => StorageTier.Conservative,
            < 32 => StorageTier.Limited,
            < 128 => StorageTier.Normal,
            _ => StorageTier.HighCapacity
        };

    private StorageCalibrationSnapshot Conservative(string reason, string? fingerprint = null)
        => new(
            StorageCalibrationPhase.Conservative,
            StorageTier.Conservative,
            fingerprint,
            null,
            _paths.SessionSpoolDirectory,
            reason);

    private void SetCurrent(StorageCalibrationSnapshot snapshot)
    {
        lock (_stateGate)
        {
            _current = snapshot;
        }

        CalibrationChanged?.Invoke(snapshot);
    }

    private string ComputeFingerprintHash()
    {
        var root = _paths.SessionSpoolDirectory;
        var input = string.Join(
            "|",
            $"schema={FingerprintSchemaVersion}",
            $"os={GetOsBucket()}",
            $"arch={RuntimeInformation.OSArchitecture}",
            $"cpu={Bucket(Environment.ProcessorCount, [1, 2, 4, 8, 16, 32])}",
            $"mem={Bucket(GetAvailableMemoryGb(), [1, 2, 4, 8, 16, 32, 64])}",
            $"root={Hash(root)}",
            $"fs={GetDriveFormat(root)}");
        return Hash(input);
    }

    private static string GetOsBucket()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "unknown";
    }

    private static int GetAvailableMemoryGb()
    {
        var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (bytes <= 0)
        {
            return 0;
        }

        return (int)Math.Max(1, bytes / 1024 / 1024 / 1024);
    }

    private static int Bucket(int value, int[] edges)
    {
        foreach (var edge in edges)
        {
            if (value <= edge)
            {
                return edge;
            }
        }

        return edges[^1] * 2;
    }

    private static string GetDriveFormat(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return "unknown";
            }

            return new DriveInfo(root).DriveFormat;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task<StoredCalibration?> TryLoadStoredAsync(CancellationToken cancellationToken)
    {
        var path = GetStatePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var state = await JsonSerializer.DeserializeAsync<StoredCalibration>(stream, cancellationToken: cancellationToken);
            return state?.SchemaVersion == CalibrationSchemaVersion ? state : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load storage calibration state.");
            return null;
        }
    }

    private async Task SaveStoredAsync(StorageCalibrationSnapshot snapshot, CancellationToken cancellationToken)
    {
        var state = new StoredCalibration
        {
            SchemaVersion = CalibrationSchemaVersion,
            FingerprintHash = snapshot.FingerprintHash ?? string.Empty,
            Tier = snapshot.Tier,
            CalibratedAtUtc = snapshot.LastCalibratedAtUtc ?? DateTime.UtcNow
        };
        var path = GetStatePath();
        var tempPath = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string GetStatePath()
        => Path.Combine(_paths.DatabaseDirectory, "storage-calibration.json");

    private async Task NotifyAsync(NotificationLevel level, string key, CancellationToken cancellationToken, params object[] args)
    {
        try
        {
            await _notificationService.AddAsync(NotificationCategory.Storage, level, key, args, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to add storage calibration notification.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class StoredCalibration
    {
        public int SchemaVersion { get; set; }
        public string FingerprintHash { get; set; } = string.Empty;
        public StorageTier Tier { get; set; } = StorageTier.Conservative;
        public DateTime CalibratedAtUtc { get; set; }
    }
}
