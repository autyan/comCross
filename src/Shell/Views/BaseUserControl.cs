using Avalonia.Controls;
using ComCross.Shared.Services;
using ComCross.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Views;

/// <summary>
/// Base class for all UserControls with controlled Shell UI workflow access.
/// </summary>
public abstract class BaseUserControl : UserControl
{
    private IShellViewContext? _shellContext;

    /// <summary>
    /// Indexer-based localization strings accessor for XAML binding: {Binding L[key]}
    /// </summary>
    public ILocalizationStrings L => ShellContext.L;

    protected IShellViewContext ShellContext
        => _shellContext ??= App.ServiceProvider.GetRequiredService<IShellViewContext>();
}
