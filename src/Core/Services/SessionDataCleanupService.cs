using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed record SessionDataCleanupTarget(string SessionId, string? PluginId);

public sealed record SessionDataCleanupResult(
    string SessionId,
    int DeletedLogFiles,
    bool PluginStorageDeleted,
    IReadOnlyList<string> Warnings);

public sealed class SessionDataCleanupService
{
    private readonly PluginSessionStorageService _pluginSessionStorage;
    private readonly AppDatabase _database;
    private readonly ILogger<SessionDataCleanupService> _logger;

    public SessionDataCleanupService(
        PluginSessionStorageService pluginSessionStorage,
        AppDatabase database,
        ILogger<SessionDataCleanupService> logger)
    {
        _pluginSessionStorage = pluginSessionStorage ?? throw new ArgumentNullException(nameof(pluginSessionStorage));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SessionDataCleanupResult>> DeleteSessionOwnedDataAsync(
        IReadOnlyList<SessionDataCleanupTarget> targets,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0)
        {
            return Array.Empty<SessionDataCleanupResult>();
        }

        var results = new List<SessionDataCleanupResult>(targets.Count);
        foreach (var target in targets)
        {
            results.Add(await DeleteSessionOwnedDataAsync(target, cancellationToken));
        }

        return results;
    }

    private async Task<SessionDataCleanupResult> DeleteSessionOwnedDataAsync(
        SessionDataCleanupTarget target,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var deletedLogFiles = 0;
        var pluginStorageDeleted = false;

        IReadOnlyList<LogFileRecord> logFiles = Array.Empty<LogFileRecord>();
        try
        {
            logFiles = await _database.GetLogFilesBySessionAsync(target.SessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            AddWarning(warnings, target.SessionId, $"Failed to read session log file index: {ex.Message}", ex);
        }

        foreach (var logFile in logFiles)
        {
            if (string.IsNullOrWhiteSpace(logFile.FilePath))
            {
                continue;
            }

            try
            {
                if (File.Exists(logFile.FilePath))
                {
                    File.Delete(logFile.FilePath);
                    deletedLogFiles++;
                }
            }
            catch (Exception ex)
            {
                AddWarning(warnings, target.SessionId, $"Failed to delete log file '{logFile.FilePath}': {ex.Message}", ex);
            }
        }

        try
        {
            await _database.RemoveLogFilesBySessionAsync(target.SessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            AddWarning(warnings, target.SessionId, $"Failed to remove session log file index: {ex.Message}", ex);
        }

        if (!string.IsNullOrWhiteSpace(target.PluginId))
        {
            try
            {
                await _pluginSessionStorage.DeleteAsync(target.PluginId, target.SessionId, cancellationToken);
                pluginStorageDeleted = true;
            }
            catch (Exception ex)
            {
                AddWarning(warnings, target.SessionId, $"Failed to delete plugin session storage: {ex.Message}", ex);
            }
        }

        return new SessionDataCleanupResult(target.SessionId, deletedLogFiles, pluginStorageDeleted, warnings);
    }

    private void AddWarning(List<string> warnings, string sessionId, string warning, Exception exception)
    {
        warnings.Add(warning);
        _logger.LogWarning(exception, "Session data cleanup warning for {SessionId}: {Warning}", sessionId, warning);
    }
}
