using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ComCross.Shared.Models;

/// <summary>
/// Represents a device connection session
/// </summary>
public sealed class Session : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private SessionStatus _status;
    private DateTime? _startTime;
    private long _rxBytes;
    private long _txBytes;
    private bool _enableDatabaseStorage;

    public required string Id { get; init; }
    
    public required string Name 
    { 
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }
    
    public required string Port { get; init; }
    public required int BaudRate { get; init; }
    
    /// <summary>
    /// Adapter ID used for this session (e.g., "serial", "plugin:com.example:tcp")
    /// </summary>
    public string AdapterId { get; set; } = "serial";
    
    /// <summary>
    /// Plugin ID if this is a plugin-backed session
    /// </summary>
    public string? PluginId { get; set; }

    /// <summary>
    /// Capability ID if this is a plugin-backed session
    /// </summary>
    public string? CapabilityId { get; set; }

    /// <summary>
    /// Additional parameters used to create this session (JSON)
    /// </summary>
    public string? ParametersJson { get; set; }

    /// <summary>
    /// Enable database storage for this session (overrides global default)
    /// Note: Historical data will not be converted. Switching may result in data loss.
    /// </summary>
    public bool EnableDatabaseStorage
    {
        get => _enableDatabaseStorage;
        set
        {
            if (_enableDatabaseStorage != value)
            {
                _enableDatabaseStorage = value;
                OnPropertyChanged();
            }
        }
    }
    
    public SessionStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }
    
    public DateTime? StartTime
    {
        get => _startTime;
        set
        {
            if (_startTime != value)
            {
                _startTime = value;
                OnPropertyChanged();
            }
        }
    }
    
    public long RxBytes
    {
        get => _rxBytes;
        set
        {
            if (_rxBytes != value)
            {
                _rxBytes = value;
                OnPropertyChanged();
            }
        }
    }
    
    public long TxBytes
    {
        get => _txBytes;
        set
        {
            if (_txBytes != value)
            {
                _txBytes = value;
                OnPropertyChanged();
            }
        }
    }
    
    public SerialSettings Settings { get; set; } = new();

    // v0.4.0: Multi-protocol support
    /// <summary>
    /// List of protocol IDs enabled for this session.
    /// Protocols can be switched dynamically during runtime.
    /// </summary>
    public List<string> ProtocolIds { get; set; } = new();

    /// <summary>
    /// Currently active protocol ID for parsing messages.
    /// If null, raw bytes view is used.
    /// </summary>
    public string? ActiveProtocolId { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum SessionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
