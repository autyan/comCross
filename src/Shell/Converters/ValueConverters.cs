using System;
using System.Globalization;
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
