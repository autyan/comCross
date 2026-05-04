using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using Avalonia;
using ComCross.Shared.Models;

namespace ComCross.Shell.ViewModels;

public sealed record LogMessageListItemContext(
    LogMessage Message,
    string? TimestampFormat,
    PayloadRenderMode PayloadRenderMode,
    MessageDisplayDensity DisplayDensity);

public sealed record MessageAttributeListItemViewModel(string Key, string Value)
{
    public string DisplayText => $"{Key}={Value}";
}

public sealed class LogMessageListItemViewModel : INotifyPropertyChanged, IInitializable<LogMessageListItemContext>
{
    private LogMessage? _message;
    private string _timestampText;
    private string _content;
    private IReadOnlyList<MessageAttributeListItemViewModel> _attributes;
    private PayloadRenderMode _payloadRenderMode;
    private MessageDisplayDensity _displayDensity;
    private bool _isInitialized;

    public LogMessageListItemViewModel()
    {
        _timestampText = string.Empty;
        _content = string.Empty;
        _attributes = Array.Empty<MessageAttributeListItemViewModel>();
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
        _payloadRenderMode = context.PayloadRenderMode;
        _displayDensity = context.DisplayDensity;
        _content = FormatDisplayContent(context.Message, _payloadRenderMode);
        _attributes = context.Message.Attributes
            .OrderBy(static x => x.Key, StringComparer.Ordinal)
            .Select(static x => new MessageAttributeListItemViewModel(x.Key, x.Value))
            .ToArray();
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

    public IReadOnlyList<MessageAttributeListItemViewModel> Attributes => _attributes;

    public bool ShowDirectionMarker => _displayDensity is MessageDisplayDensity.Slim or MessageDisplayDensity.Detailed;

    public bool ShowTimestamp => _displayDensity == MessageDisplayDensity.Detailed;

    public bool ShowSource => _displayDensity is MessageDisplayDensity.Slim or MessageDisplayDensity.Detailed;

    public bool HasVisibleAttributes => _displayDensity == MessageDisplayDensity.Detailed && _attributes.Count > 0;

    public Thickness RowPadding => _displayDensity == MessageDisplayDensity.Detailed
        ? new Thickness(8, 4)
        : new Thickness(8, 1);

    public Thickness RowBorderThickness => _displayDensity == MessageDisplayDensity.Detailed
        ? new Thickness(0, 0, 0, 1)
        : new Thickness(0);

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

    public void UpdatePayloadRenderMode(PayloadRenderMode payloadRenderMode)
    {
        if (_payloadRenderMode == payloadRenderMode)
        {
            return;
        }

        _payloadRenderMode = payloadRenderMode;
        _content = FormatDisplayContent(Message, _payloadRenderMode);
        OnPropertyChanged(nameof(Content));
    }

    public void UpdateDisplayDensity(MessageDisplayDensity displayDensity)
    {
        if (_displayDensity == displayDensity)
        {
            return;
        }

        _displayDensity = displayDensity;
        OnPropertyChanged(nameof(ShowDirectionMarker));
        OnPropertyChanged(nameof(ShowTimestamp));
        OnPropertyChanged(nameof(ShowSource));
        OnPropertyChanged(nameof(HasVisibleAttributes));
        OnPropertyChanged(nameof(RowPadding));
        OnPropertyChanged(nameof(RowBorderThickness));
    }

    private static string FormatDisplayContent(LogMessage message, PayloadRenderMode payloadRenderMode)
    {
        // System messages or text-only entries
        var raw = message.RawData;
        if (raw is null || raw.Length == 0)
        {
            return message.Content ?? string.Empty;
        }

        return payloadRenderMode == PayloadRenderMode.Hex
            ? ToHex(raw)
            : EscapeControlChars(Encoding.UTF8.GetString(raw));
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
