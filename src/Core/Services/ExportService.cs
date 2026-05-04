using System.Text;
using System.Text.Json;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Service for exporting session data
/// </summary>
public sealed class ExportService
{
    private const int ExportBatchSize = 512;

    private readonly IMessageFrameQueryService _messageFrameQuery;
    private readonly NotificationService _notificationService;
    private readonly SettingsService _settingsService;
    private readonly ComCrossPathService _paths;

    public ExportService(
        IMessageFrameQueryService messageFrameQuery,
        NotificationService notificationService,
        SettingsService settingsService,
        ComCrossPathService paths)
    {
        _messageFrameQuery = messageFrameQuery ?? throw new ArgumentNullException(nameof(messageFrameQuery));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Export session messages to a file with intelligent defaults
    /// </summary>
    public async Task<string> ExportAsync(
        Session session,
        string? searchQuery = null,
        string? customFilePath = null,
        SessionLogExportFormat? formatOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        try
        {
            var directory = ResolveExportDirectory(customFilePath);
            Directory.CreateDirectory(directory);

            var targetPath = string.IsNullOrWhiteSpace(customFilePath)
                ? BuildDefaultFilePath(session, directory)
                : customFilePath;

            var result = await ExportLiveSpoolAsync(session, targetPath, formatOverride, cancellationToken);
            var notificationKey = result.Partial
                ? "notification.export.partial"
                : "notification.export.completed";

            await _notificationService.AddAsync(
                NotificationCategory.Export,
                result.Partial ? NotificationLevel.Warning : NotificationLevel.Info,
                notificationKey,
                new object[] { Path.GetFileName(targetPath), Path.GetFullPath(targetPath) },
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

        var fallback = _paths.ExportDirectory;
        _settingsService.Current.Export.DefaultDirectory = fallback;
        _ = _settingsService.SaveAsync();
        return fallback;
    }

    private static string BuildDefaultFilePath(Session session, string directory)
    {
        var safeName = SanitizeFileName(session.Name);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{safeName}_{timestamp}.cclog");
    }

    private async Task<SessionLogExportResult> ExportLiveSpoolAsync(
        Session session,
        string targetPath,
        SessionLogExportFormat? formatOverride,
        CancellationToken cancellationToken)
    {
        var format = formatOverride ?? _settingsService.Current.Export.DefaultSessionLogFormat;
        var payloadMode = _settingsService.Current.Export.DefaultPayloadRenderMode;
        var capture = _messageFrameQuery.Query(new MessageFrameQuery(
            session.Id,
            MessageFrameDataSource.LiveSpool,
            MessageFrameQueryKind.Latest,
            0,
            1));

        var firstAvailable = capture.FirstAvailableFrameId ?? 1;
        var lastCaptured = capture.LastAvailableFrameId ?? 0;
        var bodyPath = targetPath + ".body.tmp";
        var partial = false;
        long exportedFrames = 0;

        try
        {
            await using (var body = new FileStream(bodyPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            await using (var writer = new StreamWriter(body, new UTF8Encoding(false)))
            {
                var cursor = firstAvailable - 1;
                while (cursor < lastCaptured)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var page = _messageFrameQuery.Query(new MessageFrameQuery(
                        session.Id,
                        MessageFrameDataSource.LiveSpool,
                        MessageFrameQueryKind.After,
                        cursor,
                        ExportBatchSize));
                    if (page.Status is MessageFrameQueryStatus.SourceUnavailable
                        or MessageFrameQueryStatus.InvalidQuery
                        or MessageFrameQueryStatus.ArchiveError)
                    {
                        partial = true;
                        break;
                    }

                    if (page.Status == MessageFrameQueryStatus.DataEvicted)
                    {
                        partial = true;
                        if (page.FirstAvailableFrameId is { } available && available > cursor + 1)
                        {
                            cursor = available - 1;
                        }
                    }

                    if (page.Frames.Count == 0)
                    {
                        break;
                    }

                    foreach (var frame in page.Frames)
                    {
                        if (frame.FrameId > lastCaptured)
                        {
                            break;
                        }

                        await WriteFrameAsync(writer, frame, format, payloadMode);
                        cursor = frame.FrameId;
                        exportedFrames++;
                    }
                }
            }

            await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024))
            await using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
            {
                await WriteHeaderAsync(writer, format, partial, firstAvailable, lastCaptured, exportedFrames);
                await writer.FlushAsync();
                await using var body = new FileStream(bodyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                await body.CopyToAsync(output, cancellationToken);
            }
        }
        finally
        {
            TryDeleteFile(bodyPath);
        }

        return new SessionLogExportResult(partial, exportedFrames);
    }

    private static async Task WriteHeaderAsync(
        TextWriter writer,
        SessionLogExportFormat format,
        bool partial,
        long firstFrameId,
        long lastFrameId,
        long exportedFrames)
    {
        await writer.WriteLineAsync("CCLOG/1");
        await writer.WriteLineAsync($"format: {FormatName(format)}");
        await writer.WriteLineAsync("source: LiveSpool");
        await writer.WriteLineAsync($"exportedAtUtc: {DateTime.UtcNow:O}");
        await writer.WriteLineAsync("app: ComCross");
        await writer.WriteLineAsync("contentVersion: 1");
        await writer.WriteLineAsync($"result: {(partial ? "partial" : "complete")}");
        await writer.WriteLineAsync($"firstFrameId: {firstFrameId}");
        await writer.WriteLineAsync($"lastFrameId: {lastFrameId}");
        await writer.WriteLineAsync($"exportedFrames: {exportedFrames}");
        await writer.WriteLineAsync();
    }

    private static Task WriteFrameAsync(
        TextWriter writer,
        MessageFrameRecord frame,
        SessionLogExportFormat format,
        PayloadRenderMode payloadMode)
        => format switch
        {
            SessionLogExportFormat.Slim => writer.WriteLineAsync($"{FormatDirection(frame.Direction)}\t{RenderPayload(frame.RawData, payloadMode)}"),
            SessionLogExportFormat.DetailedJsonLines => writer.WriteLineAsync(FormatDetailedJson(frame)),
            _ => writer.WriteLineAsync(RenderPayload(frame.RawData, payloadMode))
        };

    private static string FormatDetailedJson(MessageFrameRecord frame)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["frameId"] = frame.FrameId,
            ["timestampUtc"] = frame.TimestampUtc.ToString("O"),
            ["direction"] = FormatDirection(frame.Direction),
            ["source"] = frame.Source,
            ["attributes"] = frame.Attributes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ["payloadHex"] = ToHex(frame.RawData)
        });

    private static string FormatName(SessionLogExportFormat format)
        => format switch
        {
            SessionLogExportFormat.Slim => "slim",
            SessionLogExportFormat.DetailedJsonLines => "detailed-jsonl",
            _ => "plain"
        };

    private static string RenderPayload(byte[] rawData, PayloadRenderMode payloadMode)
        => payloadMode == PayloadRenderMode.Hex
            ? ToHex(rawData)
            : EscapeControlChars(Encoding.UTF8.GetString(rawData));

    private static string ToHex(byte[] data)
        => data.Length == 0 ? string.Empty : BitConverter.ToString(data).Replace("-", " ");

    private static string EscapeControlChars(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(ch) => $"\\u{(int)ch:X4}",
                _ => ch.ToString()
            });
        }

        return sb.ToString();
    }

    private static string FormatDirection(FrameDirection direction)
        => direction == FrameDirection.Tx ? "TX" : "RX";

    private static string SanitizeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return name;
    }

    private static void TryDeleteFile(string path)
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

    private sealed record SessionLogExportResult(bool Partial, long ExportedFrames);
}
