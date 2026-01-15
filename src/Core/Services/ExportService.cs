using System.Text;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Service for exporting session data
/// </summary>
public sealed class ExportService
{
    private readonly IMessageStreamService _messageStream;
    private readonly NotificationService _notificationService;
    private readonly SettingsService _settingsService;

    public ExportService(
        IMessageStreamService messageStream,
        NotificationService notificationService,
        SettingsService settingsService)
    {
        _messageStream = messageStream ?? throw new ArgumentNullException(nameof(messageStream));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <summary>
    /// Export session messages to a file with intelligent defaults
    /// </summary>
    public async Task<string> ExportAsync(
        Session session,
        string? searchQuery = null,
        string? customFilePath = null,
        CancellationToken cancellationToken = default)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        try
        {
            // Resolve export settings
            var directory = ResolveExportDirectory(customFilePath);
            var format = ResolveExportFormat(customFilePath);
            
            // Create directory if needed
            Directory.CreateDirectory(directory);

            // Build file path
            var targetPath = string.IsNullOrWhiteSpace(customFilePath)
                ? BuildDefaultFilePath(session, directory, format)
                : customFilePath;

            // Get messages (filtered by search if provided)
            var messages = string.IsNullOrWhiteSpace(searchQuery)
                ? _messageStream.GetMessages(session.Id, 0, int.MaxValue)
                : _messageStream.Search(session.Id, searchQuery);

            // Apply range settings
            messages = ApplyExportRange(messages);

            // Export based on format
            var content = FormatMessages(messages, format);
            await File.WriteAllTextAsync(targetPath, content, Encoding.UTF8, cancellationToken);

            await _notificationService.AddAsync(
                NotificationCategory.Export,
                NotificationLevel.Info,
                "notification.export.completed",
                new object[] { Path.GetFileName(targetPath) },
                cancellationToken);

            return targetPath;
        }
        catch (Exception ex)
        {
            await _notificationService.AddAsync(
                NotificationCategory.Export,
                NotificationLevel.Error,
                "notification.export.failed",
                new object[] { ex.Message },
                cancellationToken);
            throw;
        }
    }

    private string ResolveExportDirectory(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                // Update settings with new directory
                _settingsService.Current.Export.DefaultDirectory = directory;
                _ = _settingsService.SaveAsync();
                return directory;
            }
        }

        var directorySetting = _settingsService.Current.Export.DefaultDirectory;
        if (!string.IsNullOrWhiteSpace(directorySetting))
        {
            return directorySetting;
        }

        // Fallback to default directory
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross",
            "exports");
        _settingsService.Current.Export.DefaultDirectory = fallback;
        _ = _settingsService.SaveAsync();
        return fallback;
    }

    private string ResolveExportFormat(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var extension = Path.GetExtension(filePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension.TrimStart('.');
            }
        }

        return _settingsService.Current.Export.DefaultFormat;
    }

    private static string BuildDefaultFilePath(Session session, string directory, string format)
    {
        var safeName = SanitizeFileName(session.Name);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{safeName}_{timestamp}.{format}");
    }

    private IReadOnlyList<LogMessage> ApplyExportRange(IReadOnlyList<LogMessage> source)
    {
        var settings = _settingsService.Current.Export;
        if (settings.RangeMode != ExportRangeMode.Latest || settings.RangeCount <= 0)
        {
            return source;
        }

        if (source.Count <= settings.RangeCount)
        {
            return source;
        }

        return source.Skip(source.Count - settings.RangeCount).ToList();
    }

    private static string FormatMessages(IReadOnlyList<LogMessage> messages, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => FormatAsJson(messages),
            "csv" => FormatAsCsv(messages),
            _ => FormatAsText(messages)
        };
    }

    private static string FormatAsText(IReadOnlyList<LogMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            sb.AppendLine($"{msg.Timestamp:O}\t{msg.Level}\t{msg.Source}\t{msg.Content}");
        }
        return sb.ToString();
    }

    private static string FormatAsCsv(IReadOnlyList<LogMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Level,Source,Content");
        
        foreach (var msg in messages)
        {
            var content = msg.Content.Replace("\"", "\"\"");
            sb.AppendLine($"\"{msg.Timestamp:O}\",\"{msg.Level}\",\"{msg.Source}\",\"{content}\"");
        }
        
        return sb.ToString();
    }

    private static string FormatAsJson(IReadOnlyList<LogMessage> messages)
    {
        return System.Text.Json.JsonSerializer.Serialize(messages, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
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
