using System;
using System.ComponentModel;
using System.Text;
using System.Runtime.CompilerServices;
using ComCross.Shared.Models;

namespace ComCross.Shell.ViewModels;

public sealed record LogMessageListItemContext(LogMessage Message, string? TimestampFormat, bool IsHexDisplayMode);

public sealed class LogMessageListItemViewModel : INotifyPropertyChanged, IInitializable<LogMessageListItemContext>
{
    private LogMessage? _message;
    private string _timestampText;
    private string _content;
    private bool _isHexDisplayMode;
    private bool _isInitialized;

    public LogMessageListItemViewModel()
    {
        _timestampText = string.Empty;
        _content = string.Empty;
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
        _isHexDisplayMode = context.IsHexDisplayMode;
        _content = FormatDisplayContent(context.Message, _isHexDisplayMode);
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LogMessage Message => _message ?? throw new InvalidOperationException("LogMessageListItemViewModel not initialized.");

    public DateTime Timestamp => Message.Timestamp;

    public string Source
    {
        get
        {
            var source = Message.Source ?? string.Empty;
            if (source.Equals("tx", StringComparison.OrdinalIgnoreCase))
            {
                return "TX";
            }
            if (source.Equals("rx", StringComparison.OrdinalIgnoreCase))
            {
                return "RX";
            }

            return source;
        }
    }

    public string Content => _content;

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

    public void UpdateDisplayMode(bool isHexDisplayMode)
    {
        if (_isHexDisplayMode == isHexDisplayMode)
        {
            return;
        }

        _isHexDisplayMode = isHexDisplayMode;
        _content = FormatDisplayContent(Message, _isHexDisplayMode);
        OnPropertyChanged(nameof(Content));
    }

    private static string FormatDisplayContent(LogMessage message, bool isHexDisplayMode)
    {
        // System messages or text-only entries
        var raw = message.RawData;
        if (raw is null || raw.Length == 0)
        {
            return message.Content ?? string.Empty;
        }

        var source = message.Source ?? string.Empty;
        var isTx = source.Equals("TX", StringComparison.OrdinalIgnoreCase);
        var isRx = source.Equals("RX", StringComparison.OrdinalIgnoreCase);

        // TX follows the send-time format; RX follows the view mode.
        var useHex = isTx
            ? message.Format == MessageFormat.Hex
            : isRx
                ? isHexDisplayMode
                : message.Format == MessageFormat.Hex;

        return useHex ? ToHex(raw) : EscapeControlChars(Encoding.UTF8.GetString(raw));
    }

    private static string ToHex(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        return BitConverter.ToString(data).Replace("-", " ");
    }

    private static string EscapeControlChars(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Prefer readable escapes for common control chars.
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(ch) => $"\\u{(int)ch:X4}",
                _ => ch.ToString()
            });
        }

        return sb.ToString();
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
