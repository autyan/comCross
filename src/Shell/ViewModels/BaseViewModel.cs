using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// Base class for all ViewModels with localization support
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;

    /// <summary>
    /// Indexer-based localization strings accessor for XAML and code
    /// </summary>
    public ILocalizationStrings L => _localization.Strings;

    /// <summary>
    /// Direct access to the localization service
    /// </summary>
    public ILocalizationService Localization => _localization;

    protected BaseViewModel(ILocalizationService localization)
    {
        _localization = localization;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
