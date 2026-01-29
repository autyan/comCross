using Avalonia.Controls;
using ComCross.Shared.Services;
using ComCross.Shell.Services;

namespace ComCross.Shell.Views;

/// <summary>
/// Base class for all UserControls with localization support via Service Locator pattern
/// </summary>
public abstract class BaseUserControl : UserControl
{
    /// <summary>
    /// Indexer-based localization strings accessor for XAML binding: {Binding L[key]}
    /// </summary>
    public ILocalizationStrings L { get; }

    protected BaseUserControl()
    {
        L = LocalizationManager.Strings;
    }
}
