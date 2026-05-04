using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public interface ISessionArchiveStore
{
    void Append(MessageFrameRecord frame);

    IReadOnlyList<MessageFrameRecord> ReadAfter(string sessionId, long afterFrameId, int maxCount);

    IReadOnlyList<MessageFrameRecord> ReadBefore(string sessionId, long beforeFrameId, int maxCount);

    IReadOnlyList<MessageFrameRecord> ReadLatest(string sessionId, int maxCount);

    (long FirstAvailableFrameId, long LastFrameId) GetWindowInfo(string sessionId);

    bool HasArchive(string sessionId);

    bool Delete(string sessionId);
}

public sealed class SessionArchiveStore : ISessionArchiveStore
{
    private const int SchemaVersion = 1;
    private const string DefaultWorkspaceId = "default";

    private readonly ComCrossPathService _paths;
    private readonly ILogger<SessionArchiveStore> _logger;

    public SessionArchiveStore(ComCrossPathService paths, ILogger<SessionArchiveStore> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Append(MessageFrameRecord frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        using var connection = OpenSessionConnection(frame.SessionId, create: true);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO frames (
                frame_id,
                session_id,
                timestamp_utc_ms,
                direction,
                format,
                source,
                raw_data,
                byte_count,
                attributes_json,
                attribute_schema_version,
                created_at_utc_ms
            )
            VALUES (
                $frameId,
                $sessionId,
                $timestampUtcMs,
                $direction,
                $format,
                $source,
                $rawData,
                $byteCount,
                $attributesJson,
                $attributeSchemaVersion,
                $createdAtUtcMs
            );
            """;
        command.Parameters.AddWithValue("$frameId", frame.FrameId);
        command.Parameters.AddWithValue("$sessionId", frame.SessionId);
        command.Parameters.AddWithValue("$timestampUtcMs", ToUnixTimeMilliseconds(frame.TimestampUtc));
        command.Parameters.AddWithValue("$direction", frame.Direction.ToString());
        command.Parameters.AddWithValue("$format", frame.Format.ToString());
        command.Parameters.AddWithValue("$source", frame.Source);
        command.Parameters.Add("$rawData", SqliteType.Blob).Value = frame.RawData;
        command.Parameters.AddWithValue("$byteCount", frame.RawData.Length);
        command.Parameters.AddWithValue("$attributesJson", JsonSerializer.Serialize(frame.Attributes));
        command.Parameters.AddWithValue("$attributeSchemaVersion", frame.AttributeSchemaVersion);
        command.Parameters.AddWithValue("$createdAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<MessageFrameRecord> ReadAfter(string sessionId, long afterFrameId, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || maxCount <= 0 || !HasArchive(sessionId))
        {
            return Array.Empty<MessageFrameRecord>();
        }

        using var connection = OpenSessionConnection(sessionId, create: false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT frame_id, session_id, timestamp_utc_ms, direction, format, source, raw_data, attributes_json, attribute_schema_version
            FROM frames
            WHERE frame_id > $afterFrameId
            ORDER BY frame_id ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$afterFrameId", afterFrameId);
        command.Parameters.AddWithValue("$limit", maxCount);
        return ReadFrames(command);
    }

    public IReadOnlyList<MessageFrameRecord> ReadBefore(string sessionId, long beforeFrameId, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || beforeFrameId <= 0 || maxCount <= 0 || !HasArchive(sessionId))
        {
            return Array.Empty<MessageFrameRecord>();
        }

        using var connection = OpenSessionConnection(sessionId, create: false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT frame_id, session_id, timestamp_utc_ms, direction, format, source, raw_data, attributes_json, attribute_schema_version
            FROM frames
            WHERE frame_id < $beforeFrameId
            ORDER BY frame_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$beforeFrameId", beforeFrameId);
        command.Parameters.AddWithValue("$limit", maxCount);
        return ReadFrames(command).Reverse().ToArray();
    }

    public IReadOnlyList<MessageFrameRecord> ReadLatest(string sessionId, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || maxCount <= 0 || !HasArchive(sessionId))
        {
            return Array.Empty<MessageFrameRecord>();
        }

        using var connection = OpenSessionConnection(sessionId, create: false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT frame_id, session_id, timestamp_utc_ms, direction, format, source, raw_data, attributes_json, attribute_schema_version
            FROM frames
            ORDER BY frame_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", maxCount);
        return ReadFrames(command).Reverse().ToArray();
    }

    public (long FirstAvailableFrameId, long LastFrameId) GetWindowInfo(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !HasArchive(sessionId))
        {
            return (0, 0);
        }

        using var connection = OpenSessionConnection(sessionId, create: false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MIN(frame_id), 0), COALESCE(MAX(frame_id), 0) FROM frames;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (0, 0);
        }

        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    public bool HasArchive(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId) && File.Exists(GetDatabasePath(sessionId));

    public bool Delete(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var directory = GetSessionDirectory(sessionId);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        Directory.Delete(directory, recursive: true);
        return true;
    }

    private SqliteConnection OpenSessionConnection(string sessionId, bool create)
    {
        var directory = GetSessionDirectory(sessionId);
        if (!create && !File.Exists(GetDatabasePath(sessionId)))
        {
            throw new FileNotFoundException("Session archive database does not exist.", GetDatabasePath(sessionId));
        }

        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection($"Data Source={GetDatabasePath(sessionId)}");
        connection.Open();

        try
        {
            EnsureSchema(connection, sessionId);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void EnsureSchema(SqliteConnection connection, string sessionId)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS archive_info (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS frames (
                    frame_id INTEGER PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    timestamp_utc_ms INTEGER NOT NULL,
                    direction TEXT NOT NULL,
                    format TEXT NOT NULL,
                    source TEXT NOT NULL,
                    raw_data BLOB NOT NULL,
                    byte_count INTEGER NOT NULL,
                    attributes_json TEXT NOT NULL,
                    attribute_schema_version INTEGER NOT NULL,
                    created_at_utc_ms INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_frames_time ON frames(timestamp_utc_ms);
                CREATE INDEX IF NOT EXISTS idx_frames_direction_id ON frames(direction, frame_id);
                """;
            command.ExecuteNonQuery();
        }

        var schemaVersion = ReadInfo(connection, "schema_version");
        if (schemaVersion is not null && schemaVersion != SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException($"Unsupported session archive schema version: {schemaVersion}");
        }

        var storedSessionId = ReadInfo(connection, "session_id");
        if (storedSessionId is not null && !string.Equals(storedSessionId, sessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Session archive identity mismatch.");
        }

        UpsertInfo(connection, "schema_version", SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        UpsertInfo(connection, "workspace_id", DefaultWorkspaceId);
        UpsertInfo(connection, "session_id", sessionId);
    }

    private static string? ReadInfo(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM archive_info WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void UpsertInfo(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO archive_info (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<MessageFrameRecord> ReadFrames(SqliteCommand command)
    {
        var frames = new List<MessageFrameRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                frames.Add(new MessageFrameRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    FromUnixTimeMilliseconds(reader.GetInt64(2)),
                    Enum.Parse<FrameDirection>(reader.GetString(3)),
                    (byte[])reader["raw_data"],
                    Enum.Parse<MessageFormat>(reader.GetString(4)),
                    reader.GetString(5),
                    ReadAttributes(reader.GetString(7)),
                    reader.GetInt32(8)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipped unreadable archived frame.");
            }
        }

        return frames;
    }

    private static IReadOnlyDictionary<string, string> ReadAttributes(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? MessageFrameAttributes.Empty;

    private string GetSessionDirectory(string sessionId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId))).ToLowerInvariant();
        return Path.Combine(_paths.SessionArchiveDirectory, DefaultWorkspaceId, hash);
    }

    private string GetDatabasePath(string sessionId)
        => Path.Combine(GetSessionDirectory(sessionId), "archive.db");

    private static long ToUnixTimeMilliseconds(DateTime timestampUtc)
        => new DateTimeOffset(timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime()).ToUnixTimeMilliseconds();

    private static DateTime FromUnixTimeMilliseconds(long milliseconds)
        => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
}
