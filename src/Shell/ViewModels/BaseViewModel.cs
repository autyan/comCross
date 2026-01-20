using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// Base class for all ViewModels with localization support
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly EventHandler<string> _languageChangedHandler;
    private bool _isDisposed;

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

        _languageChangedHandler = (_, _) =>
        {
            // Explicitly notify L so bindings like {Binding L[some.key]} re-evaluate.
            OnPropertyChanged(nameof(L));

            // Also refresh any computed properties on the ViewModel.
            OnPropertyChanged(null);
        };

        // Ensure all bindings refresh on language changes.
        // This covers:
        // - XAML binding to ViewModel properties that compute values from L[...]
        // - Any cached string properties when they are exposed via computed getters
        _localization.LanguageChanged += _languageChangedHandler;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (disposing)
        {
            _localization.LanguageChanged -= _languageChangedHandler;
        }
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
