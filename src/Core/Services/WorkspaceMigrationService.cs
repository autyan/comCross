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
        
        // v0.3.x → v0.4.0: Introduce Workload abstraction
        if (version.StartsWith("0.3") || string.IsNullOrEmpty(state.Version))
        {
            MigrateFrom_v0_3(state);
        }
        
        // Update version
        state.Version = "0.4.0";
        
        _logger.LogInformation("Migration completed. Workspace is now at version 0.4.0");
        
        return state;
    }
    
    /// <summary>
    /// Migrate from v0.3.x to v0.4.0.
    /// In v0.3, sessions were directly under Workspace.
    /// In v0.4, we introduce Workload layer: Workspace → Workload → Session
    /// </summary>
    private void MigrateFrom_v0_3(WorkspaceState state)
    {
        _logger.LogInformation("Migrating from v0.3 to v0.4...");
        
        // If there are sessions in the old format, migrate them
        #pragma warning disable CS0618 // Type or member is obsolete
        if (state.Sessions != null && state.Sessions.Count > 0)
        {
            _logger.LogInformation("Found {Count} sessions in v0.3 format", state.Sessions.Count);
            
            // Create default workload if it doesn't exist
            var defaultWorkload = state.GetDefaultWorkload();
            if (defaultWorkload == null)
            {
                defaultWorkload = Workload.Create("默认任务", isDefault: true);
                state.Workloads.Insert(0, defaultWorkload);
                _logger.LogInformation("Created default workload for migration");
            }
            
            // Migrate all sessions to the default workload
            foreach (var session in state.Sessions)
            {
                if (!string.IsNullOrEmpty(session.Id))
                {
                    defaultWorkload.AddSession(session.Id);
                }
            }
            
            _logger.LogInformation("Migrated {Count} sessions to default workload", state.Sessions.Count);
            
            // Clear the old sessions list (but keep it for backward compatibility)
            // Don't set to null - just clear it to preserve the property
            state.Sessions.Clear();
        }
        #pragma warning restore CS0618
        
        // Ensure default workload exists even if there were no sessions
        if (state.GetDefaultWorkload() == null)
        {
            var defaultWorkload = Workload.Create("默认任务", isDefault: true);
            state.Workloads.Insert(0, defaultWorkload);
            _logger.LogInformation("Created default workload (no sessions to migrate)");
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
