using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class LogStorageService
{
    private readonly IMessageStreamService _messageStream;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly AppDatabase _database;
    private readonly ConcurrentDictionary<string, SessionLogWriter> _writers = new();
    private bool _overLimitNotified;

    public LogStorageService(
        IMessageStreamService messageStream,
        SettingsService settingsService,
        NotificationService notificationService,
        AppDatabase database)
    {
        _messageStream = messageStream;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _database = database;
    }

    public void StartSession(Session session)
    {
        if (_writers.ContainsKey(session.Id))
        {
            return;
        }

        var writer = new SessionLogWriter(session, _settingsService, _database);
        var subscription = _messageStream.Subscribe(session.Id, message =>
        {
            writer.Append(message);
            _ = HandleStorageCheckAsync(writer);
        });

        writer.AttachSubscription(subscription);
        _writers[session.Id] = writer;
    }

    public async Task StopSessionAsync(string sessionId)
    {
        if (_writers.TryRemove(sessionId, out var writer))
        {
            await writer.DisposeAsync();
        }
    }

    private async Task HandleStorageCheckAsync(SessionLogWriter writer)
    {
        if (!writer.ShouldCheckStorage())
        {
            return;
        }

        var settings = _settingsService.Current.Logs;
        var totalBytes = GetDirectorySize(settings.Directory);
        var maxBytes = settings.MaxTotalSizeMb * 1024L * 1024L;

        if (totalBytes <= maxBytes)
        {
            _overLimitNotified = false;
            return;
        }

        if (!_overLimitNotified)
        {
            _overLimitNotified = true;
            await _notificationService.AddAsync(
                NotificationCategory.Storage,
                NotificationLevel.Warning,
                "notification.storage.limitExceeded",
                new object[]
                {
                    Math.Round(totalBytes / 1024d / 1024d, 1),
                    settings.MaxTotalSizeMb
                });
        }

        if (settings.AutoDeleteEnabled)
        {
            var deleted = await ApplyAutoDeleteAsync(settings.Directory, maxBytes);
            if (deleted > 0)
            {
                await _notificationService.AddAsync(
                    NotificationCategory.Storage,
                    NotificationLevel.Info,
                    "notification.storage.autoDeleteApplied",
                    new object[] { deleted });
            }
        }
    }

    private async Task<int> ApplyAutoDeleteAsync(string directory, long maxBytes)
    {
        var dirInfo = new DirectoryInfo(directory);
        if (!dirInfo.Exists)
        {
            return 0;
        }

        var files = dirInfo.GetFiles("*.log")
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        var deleted = 0;
        var totalBytes = GetDirectorySize(directory);

        foreach (var file in files)
        {
            if (totalBytes <= maxBytes)
            {
                break;
            }

            try
            {
                var size = file.Length;
                file.Delete();
                totalBytes -= size;
                deleted++;
                await _database.RemoveLogFileAsync(file.FullName);
            }
            catch
            {
                // Ignore failures to keep user data safe.
            }
        }

        return deleted;
    }

    private static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long size = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                size += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore unreadable files.
            }
        }

        return size;
    }

    private sealed class SessionLogWriter : IAsyncDisposable
    {
        private readonly Session _session;
        private readonly SettingsService _settingsService;
        private readonly AppDatabase _database;
        private readonly object _lock = new();
        private IDisposable? _subscription;
        private StreamWriter? _writer;
        private LogFileRecord? _currentRecord;
        private long _currentSize;
        private long _nextStorageCheckBytes = 1024 * 1024;
        private DateTime _lastTimestamp = DateTime.UtcNow;

        public SessionLogWriter(Session session, SettingsService settingsService, AppDatabase database)
        {
            _session = session;
            _settingsService = settingsService;
            _database = database;
        }

        public void AttachSubscription(IDisposable subscription)
        {
            _subscription = subscription;
        }

        public void Append(LogMessage message)
        {
            var settings = _settingsService.Current.Logs;
            if (!settings.AutoSaveEnabled)
            {
                return;
            }

            lock (_lock)
            {
                EnsureWriter(settings);

                var line = $"{message.Timestamp:O}\t{message.Level}\t{message.Source}\t{message.Content}";
                _writer?.WriteLine(line);
                _writer?.Flush();

                var bytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                _currentSize += bytes;
                _lastTimestamp = message.Timestamp;

                var maxFileBytes = settings.MaxFileSizeMb * 1024L * 1024L;
                if (_currentSize >= maxFileBytes)
                {
                    Rotate();
                }
            }
        }

        public bool ShouldCheckStorage()
        {
            lock (_lock)
            {
                if (_currentSize < _nextStorageCheckBytes)
                {
                    return false;
                }

                _nextStorageCheckBytes = _currentSize + (1024 * 1024);
                return true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            IDisposable? subscription;
            LogFileRecord? record;
            StreamWriter? writer;

            lock (_lock)
            {
                subscription = _subscription;
                _subscription = null;
                record = _currentRecord;
                writer = _writer;
                _writer = null;
                _currentRecord = null;
            }

            subscription?.Dispose();
            if (writer != null)
            {
                await writer.FlushAsync();
                writer.Dispose();
            }

            if (record != null)
            {
                record.EndTime = _lastTimestamp;
                record.SizeBytes = _currentSize;
                await _database.UpsertLogFileAsync(record);
            }
        }

        private void EnsureWriter(LogSettings settings)
        {
            if (_writer != null)
            {
                return;
            }

            var directory = ResolveDirectory(settings);
            Directory.CreateDirectory(directory);
            var filePath = CreateFilePath(directory, _session.Name, _session.Id);

            _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
            _currentSize = new FileInfo(filePath).Length;
            _currentRecord = new LogFileRecord
            {
                SessionId = _session.Id,
                SessionName = _session.Name,
                FilePath = filePath,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                SizeBytes = _currentSize
            };
        }

        private void Rotate()
        {
            if (_writer == null || _currentRecord == null)
            {
                return;
            }

            _writer.Flush();
            _writer.Dispose();
            _writer = null;

            _currentRecord.EndTime = _lastTimestamp;
            _currentRecord.SizeBytes = _currentSize;
            var completedRecord = _currentRecord;

            _currentRecord = null;
            _currentSize = 0;
            _nextStorageCheckBytes = 1024 * 1024;

            _ = Task.Run(async () => await _database.UpsertLogFileAsync(completedRecord));
        }

        private static string CreateFilePath(string directory, string sessionName, string sessionId)
        {
            var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(sessionName) ? sessionId : sessionName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"{safeName}_{timestamp}.log";
            return Path.Combine(directory, fileName);
        }

        private static string ResolveDirectory(LogSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Directory))
            {
                return settings.Directory;
            }

            var baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ComCross",
                "logs"
            );
            settings.Directory = baseDirectory;
            return baseDirectory;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(ch, '_');
            }

            return name;
        }
    }
}
