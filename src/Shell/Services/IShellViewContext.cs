using Avalonia.Controls;
using ComCross.Shared.Services;

namespace ComCross.Shell.Services;

public interface IShellViewContext
{
    ILocalizationStrings L { get; }

    IMessageDialogService Dialogs { get; }

    IConnectDialogService ConnectDialogs { get; }

    ISessionRenameDialogService SessionRenameDialogs { get; }

    ITestConnectDialogService TestConnectDialogs { get; }

    Window? GetOwner(Control source);
}
