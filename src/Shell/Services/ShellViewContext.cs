using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ComCross.Shared.Services;

namespace ComCross.Shell.Services;

public sealed class ShellViewContext : IShellViewContext
{
    private readonly ILocalizationService _localization;

    public ShellViewContext(
        ILocalizationService localization,
        IMessageDialogService dialogs,
        IConnectDialogService connectDialogs,
        ISessionRenameDialogService sessionRenameDialogs,
        ITestConnectDialogService testConnectDialogs)
    {
        _localization = localization;
        Dialogs = dialogs;
        ConnectDialogs = connectDialogs;
        SessionRenameDialogs = sessionRenameDialogs;
        TestConnectDialogs = testConnectDialogs;
    }

    public ILocalizationStrings L => _localization.Strings;

    public IMessageDialogService Dialogs { get; }

    public IConnectDialogService ConnectDialogs { get; }

    public ISessionRenameDialogService SessionRenameDialogs { get; }

    public ITestConnectDialogService TestConnectDialogs { get; }

    public Window? GetOwner(Control source)
    {
        if (source is Window window)
        {
            return window;
        }

        if (TopLevel.GetTopLevel(source) is Window owner)
        {
            return owner;
        }

        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
