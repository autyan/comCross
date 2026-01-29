using Avalonia.Controls;
using ComCross.Shared.Services;
using ComCross.Shell.Services;

namespace ComCross.Shell.Views;

/// <summary>
/// Base class for all Windows with localization support via Service Locator pattern
/// </summary>
public abstract class BaseWindow : Window
{
    /// <summary>
    /// Indexer-based localization strings accessor for XAML binding: {Binding L[key]}
    /// </summary>
    public ILocalizationStrings L { get; }

    protected BaseWindow()
    {
        L = LocalizationManager.Strings;
    }
}
