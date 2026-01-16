using Microsoft.Data.Sqlite;

namespace ComCross.Core.Services;

/// <summary>
/// Main database service for program-related data (configuration, plugins, preferences).
/// Manages comcross.db which contains stable, infrequently-changing data.
/// </summary>
public sealed class MainDatabaseService : IDisposable, IAsyncDisposable
{
    private readonly string _databasePath;
    private SqliteConnection? _connection;
    private bool _disposed;

    public MainDatabaseService(string? configDirectory = null)
    {
        var baseDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross"
        );

        Directory.CreateDirectory(baseDirectory);
        _databasePath = Path.Combine(baseDirectory, "comcross.db");
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
    /// Initializes the main database with required schema and WAL mode.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        await _connection.OpenAsync(cancellationToken);

        // Initialize schema
        await InitializeSchemaAsync(cancellationToken);

        // Configure WAL mode for better concurrency
        await ConfigureWALModeAsync(cancellationToken);
    }

    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            // Application configuration table
            """
            CREATE TABLE IF NOT EXISTS app_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at INTEGER NOT NULL
            );
            """,

            // Plugin registry table
            """
            CREATE TABLE IF NOT EXISTS plugin_registry (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                version TEXT NOT NULL,
                assembly_path TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                registered_at INTEGER NOT NULL
            );
            """,

            // User preferences table
            """
            CREATE TABLE IF NOT EXISTS user_preferences (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at INTEGER NOT NULL
            );
            """,

            // Workspace index table
            """
            CREATE TABLE IF NOT EXISTS workspace_index (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                db_path TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                last_opened_at INTEGER NOT NULL
            );
            """,

            // Create indexes for common queries
            """
            CREATE INDEX IF NOT EXISTS idx_plugin_enabled 
            ON plugin_registry(enabled);
            """,

            """
            CREATE INDEX IF NOT EXISTS idx_workspace_last_opened 
            ON workspace_index(last_opened_at DESC);
            """
        };

        foreach (var sql in commands)
        {
            await using var command = _connection!.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task ConfigureWALModeAsync(CancellationToken cancellationToken)
    {
        // Enable WAL mode for better concurrency
        await ExecuteNonQueryAsync("PRAGMA journal_mode = WAL", cancellationToken);

        // Set synchronous mode to NORMAL for better performance
        await ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL", cancellationToken);

        // Set cache size (8MB)
        await ExecuteNonQueryAsync("PRAGMA cache_size = -8000", cancellationToken);

        // Enable foreign keys
        await ExecuteNonQueryAsync("PRAGMA foreign_keys = ON", cancellationToken);
    }

    /// <summary>
    /// Gets an application configuration value.
    /// </summary>
    public async Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_config WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString();
    }

    /// <summary>
    /// Sets an application configuration value.
    /// </summary>
    public async Task SetConfigAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_config (key, value, updated_at)
            VALUES ($key, $value, $time)
            ON CONFLICT(key) DO UPDATE SET 
                value = excluded.value,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a user preference value.
    /// </summary>
    public async Task<string?> GetPreferenceAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = "SELECT value FROM user_preferences WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString();
    }

    /// <summary>
    /// Sets a user preference value.
    /// </summary>
    public async Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_preferences (key, value, updated_at)
            VALUES ($key, $value, $time)
            ON CONFLICT(key) DO UPDATE SET 
                value = excluded.value,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
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
