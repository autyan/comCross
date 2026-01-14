using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ComCross.Shell.Converters;

public sealed class TimestampFormatConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not DateTime timestamp)
        {
            return string.Empty;
        }

        var format = values[1] as string;
        if (string.IsNullOrWhiteSpace(format))
        {
            format = "HH:mm:ss.fff";
        }

        return timestamp.ToString(format, culture);
    }
}
