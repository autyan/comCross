using Microsoft.Data.Sqlite;
using System.Text.Json;
using ComCross.Core.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Workspace database service for work-related data (workloads, sessions, messages).
/// Manages workspace.db which contains frequently-changing, high-volume data.
/// </summary>
public sealed class WorkspaceDatabaseService : IDisposable, IAsyncDisposable
{
    private readonly string _databasePath;
    private SqliteConnection? _connection;
    private bool _disposed;

    public WorkspaceDatabaseService(string? configDirectory = null)
    {
        var baseDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross"
        );

        Directory.CreateDirectory(baseDirectory);
        _databasePath = Path.Combine(baseDirectory, "workspace.db");
    }

    /// <summary>
    /// Gets the active database connection.
    /// </summary>
    public SqliteConnection Connection
    {
        get
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                throw new InvalidOperationException(
                    "Database not initialized. Call InitializeAsync first.");
            }
            return _connection;
        }
    }

    /// <summary>
    /// Gets the database file path.
    /// </summary>
    public string DatabasePath => _databasePath;

    /// <summary>
    /// Initializes the workspace database with required schema and optimized settings.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        await _connection.OpenAsync(cancellationToken);

        // Initialize schema
        await InitializeSchemaAsync(cancellationToken);

        // Configure for high-performance writes
        await ConfigurePerformanceAsync(cancellationToken);
    }

    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            // Workloads table
            """
            CREATE TABLE IF NOT EXISTS workloads (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                is_default INTEGER NOT NULL DEFAULT 0,
                description TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            """,

            // Sessions table
            """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                workload_id TEXT NOT NULL,
                name TEXT NOT NULL,
                port TEXT NOT NULL,
                baud_rate INTEGER NOT NULL,
                settings_json TEXT NOT NULL,
                protocol_ids TEXT,
                active_protocol_id TEXT,
                created_at INTEGER NOT NULL,
                FOREIGN KEY (workload_id) REFERENCES workloads(id) ON DELETE CASCADE
            );
            """,

            // Messages table (stores physical frames only)
            """
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                direction TEXT NOT NULL,
                raw_data BLOB NOT NULL,
                byte_count INTEGER NOT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );
            """,

            // Session metadata table
            """
            CREATE TABLE IF NOT EXISTS session_metadata (
                session_id TEXT PRIMARY KEY,
                rx_bytes INTEGER NOT NULL DEFAULT 0,
                tx_bytes INTEGER NOT NULL DEFAULT 0,
                message_count INTEGER NOT NULL DEFAULT 0,
                last_activity INTEGER NOT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );
            """,

            // Create indexes for common queries
            """
            CREATE INDEX IF NOT EXISTS idx_sessions_workload 
            ON sessions(workload_id);
            """,

            """
            CREATE INDEX IF NOT EXISTS idx_messages_session 
            ON messages(session_id);
            """,

            """
            CREATE INDEX IF NOT EXISTS idx_messages_timestamp 
            ON messages(timestamp);
            """,

            """
            CREATE INDEX IF NOT EXISTS idx_messages_session_timestamp 
            ON messages(session_id, timestamp DESC);
            """
        };

        foreach (var sql in commands)
        {
            await using var command = _connection!.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task ConfigurePerformanceAsync(CancellationToken cancellationToken)
    {
        // Enable WAL mode for concurrent reads/writes
        await ExecuteNonQueryAsync("PRAGMA journal_mode = WAL", cancellationToken);

        // Set synchronous mode to NORMAL for better performance
        await ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL", cancellationToken);

        // Larger cache size for workspace data (64MB)
        await ExecuteNonQueryAsync("PRAGMA cache_size = -64000", cancellationToken);

        // Enable foreign keys
        await ExecuteNonQueryAsync("PRAGMA foreign_keys = ON", cancellationToken);

        // Optimize for write-heavy workload
        await ExecuteNonQueryAsync("PRAGMA temp_store = MEMORY", cancellationToken);
    }

    /// <summary>
    /// Inserts a workload into the database.
    /// </summary>
    public async Task InsertWorkloadAsync(Workload workload, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workloads (id, name, is_default, description, created_at, updated_at)
            VALUES ($id, $name, $isDefault, $description, $createdAt, $updatedAt)
            """;
        command.Parameters.AddWithValue("$id", workload.Id);
        command.Parameters.AddWithValue("$name", workload.Name);
        command.Parameters.AddWithValue("$isDefault", workload.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$description", workload.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", new DateTimeOffset(workload.CreatedAt).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$updatedAt", new DateTimeOffset(workload.UpdatedAt).ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Updates a workload in the database.
    /// </summary>
    public async Task UpdateWorkloadAsync(Workload workload, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            UPDATE workloads 
            SET name = $name, description = $description, updated_at = $updatedAt
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", workload.Id);
        command.Parameters.AddWithValue("$name", workload.Name);
        command.Parameters.AddWithValue("$description", workload.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", new DateTimeOffset(workload.UpdatedAt).ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a workload from the database.
    /// </summary>
    public async Task DeleteWorkloadAsync(string workloadId, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = "DELETE FROM workloads WHERE id = $id";
        command.Parameters.AddWithValue("$id", workloadId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets all workloads from the database.
    /// </summary>
    public async Task<List<Workload>> GetAllWorkloadsAsync(CancellationToken cancellationToken = default)
    {
        var workloads = new List<Workload>();

        await using var command = Connection.CreateCommand();
        command.CommandText = "SELECT * FROM workloads ORDER BY created_at";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            workloads.Add(new Workload
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                IsDefault = reader.GetInt32(2) == 1,
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)).UtcDateTime,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)).UtcDateTime
            });
        }

        return workloads;
    }

    /// <summary>
    /// Inserts a session into the database.
    /// </summary>
    public async Task InsertSessionAsync(string id, string workloadId, string name, string port, int baudRate, 
        string? protocolIds = null, string? activeProtocolId = null, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (id, workload_id, name, port, baud_rate, settings_json, protocol_ids, active_protocol_id, created_at)
            VALUES ($id, $workloadId, $name, $port, $baudRate, $settingsJson, $protocolIds, $activeProtocolId, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$workloadId", workloadId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$port", port);
        command.Parameters.AddWithValue("$baudRate", baudRate);
        command.Parameters.AddWithValue("$settingsJson", "{}"); // Empty JSON for tests
        command.Parameters.AddWithValue("$protocolIds", protocolIds ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$activeProtocolId", activeProtocolId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts a raw message into the database.
    /// </summary>
    public async Task InsertMessageAsync(
        string sessionId,
        long timestamp,
        string direction,
        byte[] rawData,
        CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages (session_id, timestamp, direction, raw_data, byte_count)
            VALUES ($sessionId, $timestamp, $direction, $rawData, $byteCount)
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        command.Parameters.AddWithValue("$direction", direction);
        command.Parameters.AddWithValue("$rawData", rawData);
        command.Parameters.AddWithValue("$byteCount", rawData.Length);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts multiple messages in a single transaction (for batch writes).
    /// </summary>
    public async Task InsertMessageBatchAsync(
        IEnumerable<(string SessionId, long Timestamp, string Direction, byte[] RawData)> messages,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await Connection.BeginTransactionAsync(cancellationToken) as SqliteTransaction;

        try
        {
            foreach (var msg in messages)
            {
                await using var command = Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO messages (session_id, timestamp, direction, raw_data, byte_count)
                    VALUES ($sessionId, $timestamp, $direction, $rawData, $byteCount)
                    """;
                command.Parameters.AddWithValue("$sessionId", msg.SessionId);
                command.Parameters.AddWithValue("$timestamp", msg.Timestamp);
                command.Parameters.AddWithValue("$direction", msg.Direction);
                command.Parameters.AddWithValue("$rawData", msg.RawData);
                command.Parameters.AddWithValue("$byteCount", msg.RawData.Length);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Gets messages for a session within a time range.
    /// </summary>
    public async Task<List<(long Id, long Timestamp, string Direction, byte[] RawData)>> GetMessagesAsync(
        string sessionId,
        int limit = 1000,
        long? afterId = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<(long, long, string, byte[])>();

        await using var command = Connection.CreateCommand();
        
        if (afterId.HasValue)
        {
            command.CommandText = """
                SELECT id, timestamp, direction, raw_data
                FROM messages
                WHERE session_id = $sessionId AND id > $afterId
                ORDER BY id
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$afterId", afterId.Value);
        }
        else
        {
            command.CommandText = """
                SELECT id, timestamp, direction, raw_data
                FROM messages
                WHERE session_id = $sessionId
                ORDER BY id DESC
                LIMIT $limit
                """;
        }

        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add((
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                (byte[])reader.GetValue(3)
            ));
        }

        return messages;
    }

    /// <summary>
    /// Executes a non-query SQL command.
    /// </summary>
    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _connection?.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        _disposed = true;
    }
}
