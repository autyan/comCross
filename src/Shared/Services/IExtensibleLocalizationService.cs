namespace ComCross.Shared.Services;

/// <summary>
/// Optional extension interface for registering additional translations at runtime.
/// Used for plugin-provided UI i18n bundles.
/// </summary>
public interface IExtensibleLocalizationService : ILocalizationService
{
    /// <summary>
    /// Registers translations for one or more cultures.
    ///
    /// - Existing keys are NOT overwritten by default.
    /// - Duplicate keys are reported back to the caller.
    /// </summary>
    /// <param name="source">A human-readable source label, e.g. plugin id.</param>
    /// <param name="bundlesByCulture">CultureCode -> (Key -> Value)</param>
    /// <param name="duplicateKeys">Returns culture-qualified duplicate keys that were not registered.</param>
    /// <param name="invalidKeys">Returns keys rejected due to validation rules (e.g. missing domain prefix).</param>
    /// <param name="validateKey">Optional validator; return false to reject key.</param>
    void RegisterTranslations(
        string source,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bundlesByCulture,
        out IReadOnlyList<string> duplicateKeys,
        out IReadOnlyList<string> invalidKeys,
        Func<string, bool>? validateKey = null);
}
