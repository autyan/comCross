using System.Text.Json;
using ComCross.Shared.Models;
using ComCross.Core.Models;

namespace ComCross.Core.Services;

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
    /// Sessions in this workspace (legacy, pre-v0.4).
    /// Kept for backward compatibility during migration.
    /// NOTE: In v0.4+, sessions are stored separately in SQLite with WorkloadId references.
    /// If Workload data is lost due to corruption, sessions can be restored from SQLite using WorkloadId.
    /// </summary>
    [Obsolete("Use Workloads instead. This property is only for v0.3 migration.")]
    public List<SessionState>? Sessions { get; set; }

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
    public bool EnableDatabaseStorage { get; set; }
}

public sealed class SessionState
{
    public string Id { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SerialSettings Settings { get; set; } = new();
    public bool Connected { get; set; }
    public MetricsState Metrics { get; set; } = new();
    
    /// <summary>
    /// ID of the Workload this session belongs to (v0.4+).
    /// Used for data recovery: if Workload data is corrupted/lost,
    /// sessions can be restored from SQLite using this WorkloadId.
    /// </summary>
    public string? WorkloadId { get; set; }
}

public sealed class MetricsState
{
    public long Rx { get; set; }
    public long Tx { get; set; }
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
