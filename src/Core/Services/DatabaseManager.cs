namespace ComCross.Core.Services;

/// <summary>
/// Manages both main database and workspace database connections.
/// Provides unified access to the database layer with proper lifecycle management.
/// </summary>
public sealed class DatabaseManager : IDisposable, IAsyncDisposable
{
    private MainDatabaseService? _mainDb;
    private WorkspaceDatabaseService? _workspaceDb;
    private bool _initialized;
    private bool _disposed;

    private readonly string? _configDirectory;

    public DatabaseManager(string? configDirectory = null)
    {
        _configDirectory = configDirectory;
    }

    /// <summary>
    /// Gets the main database service (program data).
    /// </summary>
    public MainDatabaseService MainDb
    {
        get
        {
            if (!_initialized || _mainDb == null)
            {
                throw new InvalidOperationException(
                    "DatabaseManager not initialized. Call InitializeAsync first.");
            }
            return _mainDb;
        }
    }

    /// <summary>
    /// Gets the workspace database service (work data).
    /// </summary>
    public WorkspaceDatabaseService WorkspaceDb
    {
        get
        {
            if (!_initialized || _workspaceDb == null)
            {
                throw new InvalidOperationException(
                    "DatabaseManager not initialized. Call InitializeAsync first.");
            }
            return _workspaceDb;
        }
    }

    /// <summary>
    /// Indicates whether the database manager has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes both databases and configures them for optimal performance.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("DatabaseManager already initialized.");
        }

        try
        {
            // Initialize main database (program data)
            _mainDb = new MainDatabaseService(_configDirectory);
            await _mainDb.InitializeAsync(cancellationToken);

            // Initialize workspace database (work data)
            _workspaceDb = new WorkspaceDatabaseService(_configDirectory);
            await _workspaceDb.InitializeAsync(cancellationToken);

            _initialized = true;
        }
        catch
        {
            // Clean up on failure
            _mainDb?.Dispose();
            _workspaceDb?.Dispose();
            _mainDb = null;
            _workspaceDb = null;
            throw;
        }
    }

    /// <summary>
    /// Performs a health check on both databases.
    /// </summary>
    public async Task<DatabaseHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return new DatabaseHealthStatus
            {
                IsHealthy = false,
                Error = "DatabaseManager not initialized"
            };
        }

        try
        {
            // Test main database
            await using (var cmd = MainDb.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync(cancellationToken);
            }

            // Test workspace database
            await using (var cmd = WorkspaceDb.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync(cancellationToken);
            }

            return new DatabaseHealthStatus
            {
                IsHealthy = true,
                MainDatabasePath = GetMainDatabasePath(),
                WorkspaceDatabasePath = WorkspaceDb.DatabasePath
            };
        }
        catch (Exception ex)
        {
            return new DatabaseHealthStatus
            {
                IsHealthy = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets the main database file path.
    /// </summary>
    private string GetMainDatabasePath()
    {
        var baseDirectory = _configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross"
        );
        return Path.Combine(baseDirectory, "comcross.db");
    }

    /// <summary>
    /// Closes all database connections gracefully.
    /// </summary>
    public async Task CloseAsync()
    {
        if (_mainDb != null)
        {
            await _mainDb.DisposeAsync();
            _mainDb = null;
        }

        if (_workspaceDb != null)
        {
            await _workspaceDb.DisposeAsync();
            _workspaceDb = null;
        }

        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _mainDb?.Dispose();
        _workspaceDb?.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_mainDb != null)
        {
            await _mainDb.DisposeAsync();
        }

        if (_workspaceDb != null)
        {
            await _workspaceDb.DisposeAsync();
        }

        _disposed = true;
    }
}

/// <summary>
/// Represents the health status of the database system.
/// </summary>
public sealed class DatabaseHealthStatus
{
    public bool IsHealthy { get; init; }
    public string? Error { get; init; }
    public string? MainDatabasePath { get; init; }
    public string? WorkspaceDatabasePath { get; init; }
}
