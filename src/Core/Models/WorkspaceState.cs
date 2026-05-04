using System.Text.Json;
using System.Text.Json.Serialization;
using ComCross.Shared.Models;

namespace ComCross.Core.Models;

/// <summary>
/// Workspace state for persistence (v0.4+)
/// </summary>
public sealed class WorkspaceState
{
    /// <summary>
    /// Version of the workspace state format.
    /// Used for migration between versions.
    /// </summary>
    public string Version { get; set; } = "0.4.0";
    
    /// <summary>
    /// Unique identifier for the workspace.
    /// </summary>
    public string WorkspaceId { get; set; } = "default";
    
    /// <summary>
    /// Workloads in this workspace (v0.4+).
    /// Each workload contains multiple sessions.
    /// </summary>
    public List<Workload> Workloads { get; set; } = new();
    
    /// <summary>
    /// ID of the currently active workload (v0.4+).
    /// UI switches to this workload on startup.
    /// </summary>
    public string? ActiveWorkloadId { get; set; }
    
    /// <summary>
    /// Persisted session definitions (v0.4+).
    /// Sessions are restored as Disconnected on startup; no auto-reconnect.
    /// ParametersJson represents the last successful connection parameters.
    /// </summary>
    public List<SessionDescriptor> SessionDescriptors { get; set; } = new();
    
    /// <summary>
    /// UI state.
    /// </summary>
    public UiState? UiState { get; set; }
    
    /// <summary>
    /// Send history (command history in send panel).
    /// </summary>
    public List<string> SendHistory { get; set; } = new();
    
    /// <summary>
    /// Get the default workload (marked with IsDefault = true).
    /// </summary>
    /// <returns>Default workload, or null if none exists</returns>
    public Workload? GetDefaultWorkload()
    {
        return Workloads.FirstOrDefault(w => w.IsDefault);
    }
    
    /// <summary>
    /// Ensure a default workload exists. If not, create one.
    /// </summary>
    public void EnsureDefaultWorkload()
    {
        if (GetDefaultWorkload() == null)
        {
            var defaultWorkload = Workload.Create("默认任务", isDefault: true);
            Workloads.Insert(0, defaultWorkload);
        }
    }
}

public sealed class SessionDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AdapterId { get; set; } = string.Empty;
    public string? PluginId { get; set; }
    public string? CapabilityId { get; set; }
    public string? ParametersJson { get; set; }
    public string? DisplayTitle { get; set; }
    public string? DisplaySubtitle { get; set; }
    public string? DisplayIcon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableDatabaseStorage { get; set; }
    public SessionArchiveState ArchiveState { get; set; } = SessionArchiveState.Disabled;
    public string? ArchiveError { get; set; }
    public bool? CanReconnect { get; set; }
    public SessionInitializationState InitializationState { get; set; } = SessionInitializationState.Pending;
    public string? InitializationError { get; set; }
    public string? LastInitializedPluginVersion { get; set; }
    public int StorageSchemaVersion { get; set; }

    public string? ParentSessionId { get; set; }
    public List<string> ManagedResourceKinds { get; set; } = new();
}

public sealed class UiState
{
    public string? ActiveSessionId { get; set; }
    public bool AutoScroll { get; set; } = true;
    public FilterState? Filters { get; set; }
    public List<string> HighlightRules { get; set; } = new();
}

public sealed class FilterState
{
    public string? Keyword { get; set; }
    public string? Regex { get; set; }
}
