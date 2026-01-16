using Avalonia.Controls;
using ComCross.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

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
        var localization = App.ServiceProvider.GetRequiredService<ILocalizationService>();
        L = localization.Strings;
    }
}
