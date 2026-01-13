using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Workspace state for persistence
/// </summary>
public sealed class WorkspaceState
{
    public string WorkspaceId { get; set; } = "default";
    public List<SessionState> Sessions { get; set; } = new();
    public UiState? UiState { get; set; }
    public List<string> SendHistory { get; set; } = new();
}

public sealed class SessionState
{
    public string Id { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SerialSettings Settings { get; set; } = new();
    public bool Connected { get; set; }
    public MetricsState Metrics { get; set; } = new();
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
