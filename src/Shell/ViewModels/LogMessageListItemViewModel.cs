using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComCross.Shared.Models;

namespace ComCross.Shell.ViewModels;

public sealed class LogMessageListItemViewModel : INotifyPropertyChanged
{
    private string _timestampText;

    public LogMessageListItemViewModel(LogMessage message, string? timestampFormat)
    {
        Message = message;
        _timestampText = FormatTimestamp(message.Timestamp, timestampFormat);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LogMessage Message { get; }

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
