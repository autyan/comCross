namespace ComCross.Shared.Services;

/// <summary>
/// Indexer-based localization strings accessor for XAML binding
/// </summary>
public interface ILocalizationStrings
{
    /// <summary>
    /// Gets a localized string by key using indexer syntax
    /// </summary>
    string this[string key] { get; }
}

/// <summary>
/// Localization service interface for i18n support
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the current culture code (e.g., "en-US", "zh-CN")
    /// </summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Sets the current culture
    /// </summary>
    void SetCulture(string cultureCode);

    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Available cultures
    /// </summary>
    IReadOnlyList<LocaleCultureInfo> AvailableCultures { get; }
    
    /// <summary>
    /// Indexer-based strings accessor for XAML binding
    /// </summary>
    ILocalizationStrings Strings { get; }
}

public record LocaleCultureInfo(string Code, string DisplayName, string NativeName);
