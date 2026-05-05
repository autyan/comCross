using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ComCross.Shared.Models;

/// <summary>
/// Represents a device connection session
/// </summary>
public sealed class Session : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _adapterId = "serial";
    private string? _pluginId;
    private string? _capabilityId;
    private string? _parametersJson;
    private string? _displayTitle;
    private string? _displaySubtitle;
    private string? _displayIcon;
    private SessionStatus _status;
    private DateTime? _startTime;
    private long _rxBytes;
    private long _txBytes;
    private SessionArchiveState _archiveState = SessionArchiveState.Disabled;
    private string? _archiveError;
    private PayloadRenderMode _payloadRenderMode = PayloadRenderMode.String;
    private MessageDisplayDensity _displayDensity = MessageDisplayDensity.Detailed;
    private string? _parentSessionId;
    private bool _canReconnect = true;
    private SessionInitializationState _initializationState = SessionInitializationState.Ready;
    private string? _initializationError;
    private IReadOnlyList<string> _managedResourceKinds = Array.Empty<string>();

    public required string Id { get; init; }
    
    public required string Name 
    { 
        get => _name;
        set
        {
            SetField(ref _name, value);
        }
    }
    
    /// <summary>
    /// Adapter ID used for this session (e.g., "serial", "plugin:com.example:tcp")
    /// </summary>
    public string AdapterId
    {
        get => _adapterId;
        set => SetField(ref _adapterId, value);
    }
    
    /// <summary>
    /// Plugin ID if this is a plugin-backed session
    /// </summary>
    public string? PluginId
    {
        get => _pluginId;
        set => SetField(ref _pluginId, value);
    }

    /// <summary>
    /// Capability ID if this is a plugin-backed session
    /// </summary>
    public string? CapabilityId
    {
        get => _capabilityId;
        set => SetField(ref _capabilityId, value);
    }

    /// <summary>
    /// Committed parameters used to create this session (JSON).
    /// Represents the last successful connection parameters.
    /// </summary>
    public string? ParametersJson
    {
        get => _parametersJson;
        set
        {
            if (SetField(ref _parametersJson, value))
            {
                OnPropertyChanged(nameof(Endpoint));
            }
        }
    }

    public string? DisplayTitle
    {
        get => _displayTitle;
        set => SetField(ref _displayTitle, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public string? DisplaySubtitle
    {
        get => _displaySubtitle;
        set
        {
            if (SetField(ref _displaySubtitle, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                OnPropertyChanged(nameof(Endpoint));
            }
        }
    }

    public string? DisplayIcon
    {
        get => _displayIcon;
        set => SetField(ref _displayIcon, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>
    /// Optional parent session id for hierarchical UI grouping (e.g., a connection session
    /// belonging to a listener session).
    /// </summary>
    public string? ParentSessionId
    {
        get => _parentSessionId;
        set => SetField(ref _parentSessionId, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public bool CanReconnect
    {
        get => _canReconnect;
        set => SetField(ref _canReconnect, value);
    }

    public SessionInitializationState InitializationState
    {
        get => _initializationState;
        set => SetField(ref _initializationState, value);
    }

    public string? InitializationError
    {
        get => _initializationError;
        set => SetField(ref _initializationError, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public IReadOnlyList<string> ManagedResourceKinds
    {
        get => _managedResourceKinds;
        set => SetField(ref _managedResourceKinds, NormalizeResourceKinds(value));
    }

    public bool HasManagedResourceKind(string resourceKind)
        => !string.IsNullOrWhiteSpace(resourceKind)
           && _managedResourceKinds.Any(kind => string.Equals(kind, resourceKind, StringComparison.Ordinal));

    /// <summary>
    /// UI-friendly endpoint label produced by the session owner.
    /// </summary>
    public string Endpoint
    {
        get
        {
            return string.IsNullOrWhiteSpace(_displaySubtitle) ? string.Empty : _displaySubtitle;
        }
    }

    public SessionArchiveState ArchiveState
    {
        get => _archiveState;
        set => SetField(ref _archiveState, value);
    }

    public string? ArchiveError
    {
        get => _archiveError;
        set => SetField(ref _archiveError, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public PayloadRenderMode PayloadRenderMode
    {
        get => _payloadRenderMode;
        set => SetField(ref _payloadRenderMode, value);
    }

    public MessageDisplayDensity DisplayDensity
    {
        get => _displayDensity;
        set => SetField(ref _displayDensity, value);
    }
    
    public SessionStatus Status
    {
        get => _status;
        set
        {
            SetField(ref _status, value);
        }
    }
    
    public DateTime? StartTime
    {
        get => _startTime;
        set
        {
            SetField(ref _startTime, value);
        }
    }
    
    public long RxBytes
    {
        get => _rxBytes;
        set
        {
            SetField(ref _rxBytes, value);
        }
    }
    
    public long TxBytes
    {
        get => _txBytes;
        set
        {
            SetField(ref _txBytes, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static IReadOnlyList<string> NormalizeResourceKinds(IReadOnlyList<string>? kinds)
    {
        if (kinds is null || kinds.Count == 0)
        {
            return Array.Empty<string>();
        }

        return kinds
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Select(kind => kind.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

}

public enum SessionStatus
{
    Disconnected,
    Connecting,
    Closing,
    Connected,
    Error
}

public enum SessionInitializationState
{
    Pending,
    Updating,
    Ready,
    Failed,
    PluginUnavailable
}
