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
