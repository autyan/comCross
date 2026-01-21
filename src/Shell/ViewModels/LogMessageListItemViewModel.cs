using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComCross.Shared.Models;

namespace ComCross.Shell.ViewModels;

public sealed record LogMessageListItemContext(LogMessage Message, string? TimestampFormat);

public sealed class LogMessageListItemViewModel : INotifyPropertyChanged, IInitializable<LogMessageListItemContext>
{
    private LogMessage? _message;
    private string _timestampText;
    private bool _isInitialized;

    public LogMessageListItemViewModel()
    {
        _timestampText = string.Empty;
    }

    public void Init(LogMessageListItemContext context)
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("LogMessageListItemViewModel already initialized.");
        }

        _isInitialized = true;
        _message = context.Message;
        _timestampText = FormatTimestamp(context.Message.Timestamp, context.TimestampFormat);
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LogMessage Message => _message ?? throw new InvalidOperationException("LogMessageListItemViewModel not initialized.");

    public DateTime Timestamp => Message.Timestamp;

    public string Source => Message.Source ?? string.Empty;

    public string Content => Message.Content ?? string.Empty;

    public string TimestampText
    {
        get => _timestampText;
        private set
        {
            if (_timestampText == value)
            {
                return;
            }

            _timestampText = value;
            OnPropertyChanged();
        }
    }

    public void UpdateTimestampFormat(string? timestampFormat)
    {
        TimestampText = FormatTimestamp(Message.Timestamp, timestampFormat);
    }

    private static string FormatTimestamp(DateTime timestamp, string? timestampFormat)
    {
        if (string.IsNullOrWhiteSpace(timestampFormat))
        {
            return timestamp.ToString("HH:mm:ss.fff");
        }

        try
        {
            return timestamp.ToString(timestampFormat);
        }
        catch
        {
            return timestamp.ToString("HH:mm:ss.fff");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
