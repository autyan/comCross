using ComCross.Core.Models;
using ComCross.Core.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Service for migrating workspace state between versions.
/// </summary>
public sealed class WorkspaceMigrationService
{
    private readonly ILogger<WorkspaceMigrationService> _logger;
    
    public WorkspaceMigrationService(ILogger<WorkspaceMigrationService> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Migrate workspace state to the current version.
    /// </summary>
    /// <param name="state">Workspace state to migrate</param>
    /// <returns>Migrated workspace state</returns>
    public WorkspaceState Migrate(WorkspaceState state)
    {
        var version = state.Version ?? "0.3.0";
        
        _logger.LogInformation("Migrating workspace from version {Version} to 0.4.0", version);
        
        EnsureDefaultWorkload(state);
        
        // Update version
        state.Version = "0.4.0";
        
        _logger.LogInformation("Migration completed. Workspace is now at version 0.4.0");
        
        return state;
    }
    
    private void EnsureDefaultWorkload(WorkspaceState state)
    {
        if (state.GetDefaultWorkload() == null)
        {
            var defaultWorkload = Workload.Create("默认任务", isDefault: true);
            state.Workloads.Insert(0, defaultWorkload);
            _logger.LogInformation("Created default workload");
        }
    }
    
    /// <summary>
    /// Check if migration is needed.
    /// </summary>
    /// <param name="state">Workspace state to check</param>
    /// <returns>True if migration is needed, false otherwise</returns>
    public bool NeedsMigration(WorkspaceState state)
    {
        var currentVersion = "0.4.0";
        var stateVersion = state.Version ?? "0.3.0";
        
        return stateVersion != currentVersion;
    }
}
