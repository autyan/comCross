using System.ComponentModel;
using ComCross.Shared.Services;

namespace ComCross.Core.Services;

/// <summary>
/// Indexer-based localization strings accessor.
/// Provides dynamic string lookup with PropertyChanged notifications for language switching.
/// </summary>
internal class LocalizationStrings : ILocalizationStrings, INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;

    public LocalizationStrings(ILocalizationService localization)
    {
        _localization = localization;
    }

    public string this[string key] => _localization.GetString(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        // Different binding engines disagree on the indexer property name.
        // Raise both to ensure XAML bindings like {Binding L[some.key]} refresh.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
