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

    /// <summary>
    /// A best-effort, UI-friendly endpoint label derived from <see cref="ParametersJson"/>.
    /// This is for display only; sessions do not own bus-specific fields.
    /// </summary>
    public string Endpoint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_parametersJson))
            {
                return string.Empty;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(_parametersJson);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return string.Empty;
                }

                // Convention-based common keys across bus adapters.
                if (TryGetString(doc.RootElement, "port", out var port))
                {
                    return port;
                }
                if (TryGetString(doc.RootElement, "host", out var host))
                {
                    return host;
                }
                if (TryGetString(doc.RootElement, "address", out var address))
                {
                    return address;
                }
                if (TryGetString(doc.RootElement, "endpoint", out var endpoint))
                {
                    return endpoint;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Enable database storage for this session (overrides global default)
    /// Note: Historical data will not be converted. Switching may result in data loss.
    /// </summary>
    public bool EnableDatabaseStorage
    {
        get => _enableDatabaseStorage;
        set
        {
            SetField(ref _enableDatabaseStorage, value);
        }
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

    private static bool TryGetString(System.Text.Json.JsonElement obj, string key, out string value)
    {
        if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var s = prop.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                value = s;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}

public enum SessionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
