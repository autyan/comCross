using System;
using System.Collections.Generic;
using System.IO;
using ComCross.Shared.Models;
using Microsoft.Data.Sqlite;

namespace ComCross.Core.Services;

public sealed class AppDatabase
{
    private readonly string _databasePath;

    public AppDatabase(string? configDirectory = null)
    {
        var baseDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross"
        );

        Directory.CreateDirectory(baseDirectory);
        _databasePath = Path.Combine(baseDirectory, "ComCross.db");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS notifications (
                id TEXT PRIMARY KEY,
                category INTEGER NOT NULL,
                message_key TEXT NOT NULL,
                message_args TEXT NOT NULL,
                level INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                is_read INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS log_files (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                session_name TEXT NOT NULL,
                file_path TEXT NOT NULL UNIQUE,
                start_time TEXT NOT NULL,
                end_time TEXT NOT NULL,
                size_bytes INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS config_history (
                id TEXT PRIMARY KEY,
                created_at TEXT NOT NULL,
                settings_json TEXT NOT NULL
            );
            """
        };

        foreach (var sql in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task InsertNotificationAsync(NotificationItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO notifications (id, category, message_key, message_args, level, created_at, is_read)
            VALUES ($id, $category, $messageKey, $messageArgs, $level, $createdAt, $isRead);
            """;
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$category", (int)item.Category);
        command.Parameters.AddWithValue("$messageKey", item.MessageKey);
        command.Parameters.AddWithValue("$messageArgs", item.MessageArgsJson);
        command.Parameters.AddWithValue("$level", (int)item.Level);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isRead", item.IsRead ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(
        int limit,
        DateTime? sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var items = new List<NotificationItem>();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        if (sinceUtc.HasValue)
        {
            command.CommandText =
                """
                SELECT id, category, message_key, message_args, level, created_at, is_read
                FROM notifications
                WHERE datetime(created_at) >= datetime($sinceUtc)
                ORDER BY datetime(created_at) DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$sinceUtc", sinceUtc.Value.ToString("O"));
        }
        else
        {
            command.CommandText =
                """
                SELECT id, category, message_key, message_args, level, created_at, is_read
                FROM notifications
                ORDER BY datetime(created_at) DESC
                LIMIT $limit;
                """;
        }
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new NotificationItem
            {
                Id = reader.GetString(0),
                Category = (NotificationCategory)reader.GetInt32(1),
                MessageKey = reader.GetString(2),
                MessageArgsJson = reader.GetString(3),
                Level = (NotificationLevel)reader.GetInt32(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                IsRead = reader.GetInt32(6) == 1
            });
        }

        return items;
    }

    public async Task MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notifications SET is_read = 1;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkNotificationReadAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notifications SET is_read = 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteNotificationAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM notifications WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAllNotificationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM notifications;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertLogFileAsync(LogFileRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO log_files (id, session_id, session_name, file_path, start_time, end_time, size_bytes)
            VALUES ($id, $sessionId, $sessionName, $filePath, $startTime, $endTime, $sizeBytes)
            ON CONFLICT(file_path) DO UPDATE SET
                end_time = excluded.end_time,
                size_bytes = excluded.size_bytes;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$sessionId", record.SessionId);
        command.Parameters.AddWithValue("$sessionName", record.SessionName);
        command.Parameters.AddWithValue("$filePath", record.FilePath);
        command.Parameters.AddWithValue("$startTime", record.StartTime.ToString("O"));
        command.Parameters.AddWithValue("$endTime", record.EndTime.ToString("O"));
        command.Parameters.AddWithValue("$sizeBytes", record.SizeBytes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveLogFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM log_files WHERE file_path = $filePath;";
        command.Parameters.AddWithValue("$filePath", filePath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertConfigHistoryAsync(string settingsJson, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO config_history (id, created_at, settings_json)
            VALUES ($id, $createdAt, $settingsJson);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$settingsJson", settingsJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }
}
