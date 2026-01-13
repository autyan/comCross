using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using ComCross.Shared.Services;
using System;
using SysGlobalization = System.Globalization;

namespace ComCross.Shell.Converters;

/// <summary>
/// Markup extension for localized strings
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // This will be resolved at runtime through the DataContext
        return new Avalonia.Data.Binding($"Localization[{Key}]");
    }
}

/// <summary>
/// Converter to access localization service from binding
/// </summary>
public class LocalizationConverter : IValueConverter
{
    private readonly ILocalizationService _localization;

    public LocalizationConverter(ILocalizationService localization)
    {
        _localization = localization;
    }

    public object? Convert(object? value, Type targetType, object? parameter, SysGlobalization.CultureInfo culture)
    {
        if (parameter is string key)
        {
            return _localization.GetString(key);
        }
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, SysGlobalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
