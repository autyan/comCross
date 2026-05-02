using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ComCross.Shell.Converters;

/// <summary>
/// Converts bool to expand icon (▼ for true, ▶ for false)
/// </summary>
public sealed class BoolToExpandIconConverter : IValueConverter
{
    public static readonly BoolToExpandIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "▼" : "▶";
        }

        return "▶";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
