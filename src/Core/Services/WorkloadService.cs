using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Service for managing workloads.
/// NOTE: WorkloadService depends on ConfigService for persistence, but NOT on WorkspaceService
/// to avoid circular dependency. WorkspaceService depends on WorkloadService.
/// </summary>
public sealed class WorkloadService
{
    private readonly ILogger<WorkloadService> _logger;
    private readonly IEventBus _eventBus;
    private readonly ConfigService _configService;
    private readonly ILocalizationService? _localizationService;
    
    public WorkloadService(
        ILogger<WorkloadService> logger,
        IEventBus eventBus,
        ConfigService configService,
        ILocalizationService? localizationService = null)
    {
        _logger = logger;
        _eventBus = eventBus;
        _configService = configService;
        _localizationService = localizationService;
    }
    
    /// <summary>
    /// Load workspace state from persistence (internal helper).
    /// </summary>
    private async Task<WorkspaceState> LoadStateAsync()
    {
        var state = await _configService.LoadWorkspaceStateAsync();
        if (state == null)
        {
            state = new WorkspaceState();
            state.EnsureDefaultWorkload();
        }
        return state;
    }
    
    /// <summary>
    /// Save workspace state to persistence (internal helper).
    /// </summary>
    private async Task SaveStateAsync(WorkspaceState state)
    {
        await _configService.SaveWorkspaceStateAsync(state);
    }
    
    /// <summary>
    /// Create a new workload.
    /// </summary>
    /// <param name="name">Workload name</param>
    /// <param name="description">Optional description</param>
    /// <returns>Created workload</returns>
    public async Task<Workload> CreateWorkloadAsync(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Workload name cannot be empty.", nameof(name));
        }
        
        var workload = Workload.Create(name, isDefault: false);
        workload.Description = description;
        
        var state = await LoadStateAsync();
        state.Workloads.Add(workload);
        await SaveStateAsync(state);
        
        _logger.LogInformation("Created workload: {WorkloadName} ({WorkloadId})", name, workload.Id);
        
        _eventBus.Publish(new WorkloadCreatedEvent(workload.Id, workload.Name));
        
        return workload;
    }
    
    /// <summary>
    /// Delete a workload.
    /// </summary>
    /// <param name="workloadId">Workload ID to delete</param>
    /// <returns>True if deleted successfully, false if workload is default or not found</returns>
    public async Task<(bool Success, string? ErrorMessage)> DeleteWorkloadAsync(string workloadId)
    {
        var state = await LoadStateAsync();
        var workload = state.Workloads.FirstOrDefault(w => w.Id == workloadId);
        
        if (workload == null)
        {
            return (false, "Workload not found.");
        }
        
        // Prevent deletion of default workload
        if (workload.IsDefault)
        {
            return (false, "Cannot delete the default workload. You can rename it instead.");
        }
        
        state.Workloads.Remove(workload);
        await SaveStateAsync(state);
        
        _logger.LogInformation("Deleted workload: {WorkloadName} ({WorkloadId})", workload.Name, workloadId);
        
        _eventBus.Publish(new WorkloadDeletedEvent(workloadId, workload.Name));
        
        return (true, null);
    }
    
    /// <summary>
    /// Rename a workload.
    /// </summary>
    /// <param name="workloadId">Workload ID</param>
    /// <param name="newName">New name</param>
    /// <returns>True if renamed successfully, false if not found</returns>
    public async Task<bool> RenameWorkloadAsync(string workloadId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Workload name cannot be empty.", nameof(newName));
        }
        
        var state = await LoadStateAsync();
        var workload = state.Workloads.FirstOrDefault(w => w.Id == workloadId);
        
        if (workload == null)
        {
            return false;
        }
        
        var oldName = workload.Name;
        workload.Rename(newName);
        await SaveStateAsync(state);
        
        _logger.LogInformation("Renamed workload: '{OldName}' → '{NewName}' ({WorkloadId})", 
            oldName, newName, workloadId);
        
        _eventBus.Publish(new WorkloadRenamedEvent(workloadId, oldName, newName));
        
        return true;
    }
    
    /// <summary>
    /// Update workload description.
    /// </summary>
    /// <param name="workloadId">Workload ID</param>
    /// <param name="description">New description</param>
    /// <returns>True if updated successfully, false if not found</returns>
    public async Task<bool> UpdateDescriptionAsync(string workloadId, string? description)
    {
        var state = await LoadStateAsync();
        var workload = state.Workloads.FirstOrDefault(w => w.Id == workloadId);
        
        if (workload == null)
        {
            return false;
        }
        
        workload.Description = description;
        workload.UpdatedAt = DateTime.UtcNow;
        await SaveStateAsync(state);
        
        _logger.LogInformation("Updated description for workload: {WorkloadName} ({WorkloadId})", 
            workload.Name, workloadId);
        
        return true;
    }
    
    /// <summary>
    /// Get all workloads.
    /// </summary>
    /// <returns>List of workloads</returns>
    public async Task<List<Workload>> GetAllWorkloadsAsync()
    {
        var state = await LoadStateAsync();
        return state.Workloads;
    }
    
    /// <summary>
    /// Get a specific workload by ID.
    /// </summary>
    /// <param name="workloadId">Workload ID</param>
    /// <returns>Workload if found, null otherwise</returns>
    public async Task<Workload?> GetWorkloadAsync(string workloadId)
    {
        var state = await LoadStateAsync();
        return state.Workloads.FirstOrDefault(w => w.Id == workloadId);
    }
    
    /// <summary>
    /// Get the default workload.
    /// </summary>
    /// <returns>Default workload if exists, null otherwise</returns>
    public async Task<Workload?> GetDefaultWorkloadAsync()
    {
        var state = await LoadStateAsync();
        return state.GetDefaultWorkload();
    }
    
    /// <summary>
    /// Add a session to a workload.
    /// </summary>
    /// <param name="workloadId">Workload ID</param>
    /// <param name="sessionId">Session ID to add</param>
    /// <returns>True if added successfully, false if workload not found</returns>
    public async Task<bool> AddSessionToWorkloadAsync(string workloadId, string sessionId)
    {
        var state = await LoadStateAsync();
        var workload = state.Workloads.FirstOrDefault(w => w.Id == workloadId);
        
        if (workload == null)
        {
            return false;
        }
        
        workload.AddSession(sessionId);
        await SaveStateAsync(state);
        
        _logger.LogInformation("Added session {SessionId} to workload {WorkloadName} ({WorkloadId})", 
            sessionId, workload.Name, workloadId);
        
        return true;
    }
    
    /// <summary>
    /// Remove a session from a workload.
    /// </summary>
    /// <param name="workloadId">Workload ID</param>
    /// <param name="sessionId">Session ID to remove</param>
    /// <returns>True if removed successfully, false if workload not found or session not in workload</returns>
    public async Task<bool> RemoveSessionFromWorkloadAsync(string workloadId, string sessionId)
    {
        var state = await LoadStateAsync();
        var workload = state.Workloads.FirstOrDefault(w => w.Id == workloadId);
        
        if (workload == null)
        {
            return false;
        }
        
        if (workload.RemoveSession(sessionId))
        {
            await SaveStateAsync(state);
            
            _logger.LogInformation("Removed session {SessionId} from workload {WorkloadName} ({WorkloadId})", 
                sessionId, workload.Name, workloadId);
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Copy a workload with a new name (includes state and data).
    /// </summary>
    /// <param name="sourceWorkloadId">Source workload ID</param>
    /// <param name="newName">New workload name</param>
    /// <returns>Copied workload if successful, null if source not found</returns>
    public async Task<Workload?> CopyWorkloadAsync(string sourceWorkloadId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Workload name cannot be empty.", nameof(newName));
        }

        var state = await LoadStateAsync();
        var sourceWorkload = state.Workloads.FirstOrDefault(w => w.Id == sourceWorkloadId);

        if (sourceWorkload == null)
        {
            return null;
        }

        // Create new workload (never default)
        var newWorkload = Workload.Create(newName, isDefault: false);
        newWorkload.Description = sourceWorkload.Description;
        
        // Copy session IDs (shallow copy - sessions themselves are shared)
        foreach (var sessionId in sourceWorkload.SessionIds)
        {
            newWorkload.AddSession(sessionId);
        }

        state.Workloads.Add(newWorkload);
        await SaveStateAsync(state);

        _logger.LogInformation("Copied workload: '{SourceName}' → '{NewName}' ({NewId})", 
            sourceWorkload.Name, newName, newWorkload.Id);

        _eventBus.Publish(new WorkloadCreatedEvent(newWorkload.Id, newWorkload.Name));

        return newWorkload;
    }

    /// <summary>
    /// Get the active workload ID.
    /// </summary>
    /// <returns>Active workload ID, or default workload ID if none set</returns>
    /// <summary>
    /// Get the active workload ID asynchronously.
    /// </summary>
    /// <returns>Active workload ID, or default workload ID if none set</returns>
    public async Task<string> GetActiveWorkloadIdAsync()
    {
        var state = await LoadStateAsync();
        
        if (!string.IsNullOrEmpty(state.ActiveWorkloadId))
        {
            // Verify the active workload still exists
            if (state.Workloads.Any(w => w.Id == state.ActiveWorkloadId))
            {
                return state.ActiveWorkloadId;
            }
        }

        // Fallback to default workload
        var defaultWorkload = state.GetDefaultWorkload();
        return defaultWorkload?.Id ?? state.Workloads.FirstOrDefault()?.Id ?? string.Empty;
    }

    /// <summary>
    /// Get the active workload ID (synchronous version - may cause deadlock on UI thread).
    /// Use GetActiveWorkloadIdAsync() instead when possible.
    /// </summary>
    /// <returns>Active workload ID, or default workload ID if none set</returns>
    [Obsolete("Use GetActiveWorkloadIdAsync() to avoid potential deadlocks on UI thread")]
    public string GetActiveWorkloadId()
    {
        var state = LoadStateAsync().GetAwaiter().GetResult();
        
        if (!string.IsNullOrEmpty(state.ActiveWorkloadId))
        {
            // Verify the active workload still exists
            if (state.Workloads.Any(w => w.Id == state.ActiveWorkloadId))
            {
                return state.ActiveWorkloadId;
            }
        }

        // Fallback to default workload
        var defaultWorkload = state.GetDefaultWorkload();
        return defaultWorkload?.Id ?? state.Workloads.FirstOrDefault()?.Id ?? string.Empty;
    }

    /// <summary>
    /// Set the active workload.
    /// </summary>
    /// <param name="workloadId">Workload ID to activate</param>
    /// <returns>True if set successfully, false if workload not found</returns>
    public bool SetActiveWorkload(string workloadId)
    {
        var state = LoadStateAsync().GetAwaiter().GetResult();
        var workload = state.Workloads.FirstOrDefault(w => w.Id == workloadId);

        if (workload == null)
        {
            return false;
        }

        state.ActiveWorkloadId = workloadId;
        SaveStateAsync(state).GetAwaiter().GetResult();

        _logger.LogInformation("Set active workload: {WorkloadName} ({WorkloadId})", 
            workload.Name, workloadId);

        return true;
    }

    /// <summary>
    /// Get a specific workload by ID (synchronous).
    /// </summary>
    /// <param name="workloadId">Workload ID</param>
    /// <returns>Workload if found, null otherwise</returns>
    public Workload? GetWorkload(string workloadId)
    {
        var state = LoadStateAsync().GetAwaiter().GetResult();
        return state.Workloads.FirstOrDefault(w => w.Id == workloadId);
    }

    /// <summary>
    /// Ensure a default workload exists. If not, create one.
    /// Called during application startup.
    /// </summary>
    public async Task EnsureDefaultWorkloadAsync()
    {
        var state = await LoadStateAsync();
        
        if (state.GetDefaultWorkload() == null)
        {
            _logger.LogInformation("No default workload found, creating one...");
            
            var username = Environment.UserName ?? "User";
            var workloadName = _localizationService != null
                ? _localizationService.GetString("workload.default.name", username)
                : $"{username}的工作区"; // Fallback if localization not available
            
            var defaultWorkload = Workload.Create(workloadName, isDefault: true);
            state.Workloads.Insert(0, defaultWorkload);
            await SaveStateAsync(state);
            
            _logger.LogInformation("Created default workload: {WorkloadId}", defaultWorkload.Id);
            
            _eventBus.Publish(new WorkloadCreatedEvent(defaultWorkload.Id, defaultWorkload.Name));
        }
    }
}
