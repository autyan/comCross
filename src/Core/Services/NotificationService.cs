using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class NotificationService
{
    private readonly AppDatabase _database;
    private readonly SettingsService _settingsService;

    public NotificationService(AppDatabase database, SettingsService settingsService)
    {
        _database = database;
        _settingsService = settingsService;
    }

    public event EventHandler<NotificationItem>? NotificationAdded;

    public async Task<IReadOnlyList<NotificationItem>> GetRecentAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var retentionDays = _settingsService.Current.Notifications.RetentionDays;
        var sinceUtc = DateTime.UtcNow.AddDays(-retentionDays);
        return await _database.GetNotificationsAsync(limit, sinceUtc, cancellationToken);
    }

    public async Task AddAsync(
        NotificationCategory category,
        NotificationLevel level,
        string messageKey,
        object[] messageArgs,
        CancellationToken cancellationToken = default)
    {
        if (!IsCategoryEnabled(category))
        {
            return;
        }

        var item = new NotificationItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Category = category,
            Level = level,
            MessageKey = messageKey,
            MessageArgsJson = JsonSerializer.Serialize(messageArgs),
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };

        await _database.InsertNotificationAsync(item, cancellationToken);
        NotificationAdded?.Invoke(this, item);
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        await _database.MarkAllNotificationsReadAsync(cancellationToken);
    }

    public async Task MarkReadAsync(string id, CancellationToken cancellationToken = default)
    {
        await _database.MarkNotificationReadAsync(id, cancellationToken);
    }

    private bool IsCategoryEnabled(NotificationCategory category)
    {
        var settings = _settingsService.Current.Notifications;
        return category switch
        {
            NotificationCategory.Storage => settings.StorageAlertsEnabled,
            NotificationCategory.Connection => settings.ConnectionAlertsEnabled,
            NotificationCategory.Export => settings.ExportAlertsEnabled,
            _ => true
        };
    }
}
