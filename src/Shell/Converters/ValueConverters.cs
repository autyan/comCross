using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ComCross.Shared.Models;

namespace ComCross.Shell.Converters;

public class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SessionStatus status)
        {
            return status switch
            {
                SessionStatus.Connected => new SolidColorBrush(Color.Parse("#2CB5A9")),
                SessionStatus.Connecting => new SolidColorBrush(Color.Parse("#F5A524")),
                SessionStatus.Error => new SolidColorBrush(Color.Parse("#E5534B")),
                _ => new SolidColorBrush(Color.Parse("#87909B"))
            };
        }
        return new SolidColorBrush(Color.Parse("#87909B"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LevelColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => new SolidColorBrush(Color.Parse("#E5534B")),
                LogLevel.Warning => new SolidColorBrush(Color.Parse("#F5A524")),
                LogLevel.Critical => new SolidColorBrush(Color.Parse("#E5534B")),
                _ => new SolidColorBrush(Color.Parse("#3AA0FF"))
            };
        }
        return new SolidColorBrush(Color.Parse("#3AA0FF"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DirectionColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string direction)
        {
            return direction switch
            {
                "TX" or "tx" => new SolidColorBrush(Color.Parse("#3AA0FF")), // Blue for transmit
                "RX" or "rx" => new SolidColorBrush(Color.Parse("#2CB5A9")), // Green for receive
                _ => new SolidColorBrush(Color.Parse("#87909B"))
            };
        }
        return new SolidColorBrush(Color.Parse("#87909B"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public sealed class IndentMarginConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0
        };

        if (level < 0)
        {
            level = 0;
        }

        // Keep vertical spacing consistent with existing template (was Margin="0,2").
        var left = level * 16;
        return new Thickness(left, 2, 0, 2);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class ListenerBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isListener = value switch
        {
            bool b => b,
            _ => false
        };

        return isListener
            ? new SolidColorBrush(Color.Parse("#1B242A"))
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
