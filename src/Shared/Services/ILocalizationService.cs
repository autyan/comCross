namespace ComCross.Shared.Services;

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
}

public record LocaleCultureInfo(string Code, string DisplayName, string NativeName);
